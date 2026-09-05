using System.Text.Json;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The <c>operationId</c> a handler publishes: the method name in camelCase by default, and what
/// <c>[Operation]</c> declares where it is written.
/// </summary>
public class OperationIdTests {

    private static GeneratorResult Run(string controllers) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    {{GeneratedOpenApiDocument.EnableAttribute}}
                    public partial class TestApplication { }

                    {{controllers}}
                    """
            },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

    /// <summary>Every operation's id, keyed "VERB /path".</summary>
    private static Dictionary<string, string> OperationIds(GeneratorResult result) {
        using var document = JsonDocument.Parse(
            GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument")));

        var ids = new Dictionary<string, string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject()) {
            foreach (var operation in path.Value.EnumerateObject()) {
                ids[operation.Name.ToUpperInvariant() + " " + path.Name] =
                    operation.Value.GetProperty("operationId").GetString()!;
            }
        }

        return ids;
    }

    [Fact]
    public void TheMethodNameInCamelCaseIsTheDefault() {
        var ids = OperationIds(Run("""
            public class TodoController {
                [Get("/todos")]
                public string All() => "";
            }
            """).AssertNoErrors());

        Assert.Equal("all", ids["GET /todos"]);
    }

    [Fact]
    public void ADeclaredIdIsPublishedAsWritten() {
        var ids = OperationIds(Run("""
            public class TodoController {
                [Get("/todos")]
                [Operation("listTodos")]
                public string All() => "";
            }
            """).AssertNoErrors());

        Assert.Equal("listTodos", ids["GET /todos"]);
    }

    [Fact]
    public void AQualifiedAttributeNameIsReadTheSameWay() {
        var ids = OperationIds(Run("""
            public class TodoController {
                [Get("/todos")]
                [Hardened.Web.Runtime.Attributes.OperationAttribute("listTodos")]
                public string All() => "";
            }
            """).AssertNoErrors());

        Assert.Equal("listTodos", ids["GET /todos"]);
    }

    /// <summary>
    /// The declared id is the contract, so the handler that merely derived the same name is the
    /// one that moves - prefixed with its tag, as two derived names are.
    /// </summary>
    [Fact]
    public void ADerivedNameYieldsToADeclaredOne() {
        var ids = OperationIds(Run("""
            public class TodoController {
                [Get("/todos")]
                [Operation("all")]
                public string List() => "";
            }

            public class OrderController {
                [Get("/orders")]
                public string All() => "";
            }
            """).AssertNoErrors());

        Assert.Equal("all", ids["GET /todos"]);
        Assert.Equal("orderAll", ids["GET /orders"]);
    }

    [Fact]
    public void TwoHandlersDeclaringOneIdIsAnError() {
        var result = Run("""
            public class TodoController {
                [Get("/todos")]
                [Operation("list")]
                public string All() => "";
            }

            public class OrderController {
                [Get("/orders")]
                [Operation("list")]
                public string All() => "";
            }
            """);

        var reported = result.GeneratorDiagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id == OpenApiDocumentDiagnostics.DuplicateOperationIdId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("\"list\"", reported.GetMessage());
        Assert.Contains("TodoController.All", reported.GetMessage());
        Assert.Contains("OrderController.All", reported.GetMessage());
    }

    [Fact]
    public void DistinctDeclaredIdsReportNothing() {
        var result = Run("""
            public class TodoController {
                [Get("/todos")]
                [Operation("listTodos")]
                public string All() => "";

                [Get("/todos/{id}")]
                [Operation("getTodo")]
                public string ById(int id) => "";
            }
            """).AssertNoErrors();

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == OpenApiDocumentDiagnostics.DuplicateOperationIdId);
    }
}
