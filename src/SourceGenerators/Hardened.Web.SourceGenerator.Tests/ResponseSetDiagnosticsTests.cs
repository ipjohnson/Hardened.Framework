using Hardened.Requests.Abstract.Responses;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What a declared case set is rejected for.
///
/// <para>
/// Both of these describe an ambiguity that lands in the shipped contract rather than in the
/// generated code. The switch compiles and runs either way, which is exactly why nothing else would
/// ever surface them - the failure is a document a client cannot read unambiguously, discovered by
/// whoever generates that client.
/// </para>
/// </summary>
public class ResponseSetDiagnosticsTests {

    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(Response<,>)];

    private static GeneratorResult Generate(string handlers) =>
        GeneratorTestHarness.Run(
            $$"""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public record Todo(int Id);
            public record Base(int Id);

            // A 404 whose schema is a superset of the 200's, which is the ambiguity.
            [HttpStatus(404)]
            public record Derived(int Id) : Base(Id);

            // The same pair with no status of its own, so both take the success status.
            public record Sibling(int Id) : Base(Id);

            public class TodoController {
            {{handlers}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    private static Diagnostic? Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// Everything is assignable to <c>object</c>, so its arm would answer for every response the
    /// handler returns and the document would describe all of them with its schema.
    /// </summary>
    [Fact]
    public void ObjectAsACaseIsAnError() {
        var reported = Reported(
            Generate("""
                [Get("/todos/{id}")]
                public Response<object, NotFound> ById(int id) => new NotFound("todo");
            """),
            ResponseModelDiagnostics.UntypedCaseId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("ById", reported.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The subtype's schema is a superset of the base's, so a payload validates against both
    /// statuses and <c>oneOf</c> requires exactly one match. No arrangement of switch arms fixes an
    /// artifact that is already ambiguous.
    /// </summary>
    [Fact]
    public void TwoAssignableCasesAtDifferentStatusesIsAnError() {
        var reported = Reported(
            Generate("""
                [Get("/todos/{id}")]
                public Response<Base, Derived, NotFound> ById(int id) => new Base(id);
            """),
            ResponseModelDiagnostics.AssignableCasesId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
    }

    /// <summary>
    /// Two cases of one status go into a single oneOf, where their relationship decides nothing.
    /// Rejecting that would forbid the shape the design calls a oneOf-within-200.
    /// </summary>
    [Fact]
    public void TwoAssignableCasesAtOneStatusAreFine() {
        var result = Generate("""
                [Get("/todos/{id}")]
                public Response<Base, Sibling> ById(int id) => new Base(id);
            """);

        Assert.Null(Reported(result, ResponseModelDiagnostics.AssignableCasesId));
    }

    /// <summary>
    /// The ordinary set, which every one of these must leave alone.
    /// </summary>
    [Fact]
    public void AWellFormedSetReportsNothing() {
        var result = Generate("""
                [Get("/todos/{id}")]
                public Response<Todo, NotFound, Conflict> ById(int id) => new Todo(id);
            """);

        Assert.Null(Reported(result, ResponseModelDiagnostics.UntypedCaseId));
        Assert.Null(Reported(result, ResponseModelDiagnostics.AssignableCasesId));
    }

    [Fact]
    public void AnOrdinaryHandlerReportsNothing() {
        var result = Generate("""
                [Get("/todos/{id}")]
                public Todo ById(int id) => new Todo(id);
            """);

        Assert.Null(Reported(result, ResponseModelDiagnostics.UntypedCaseId));
        Assert.Null(Reported(result, ResponseModelDiagnostics.AssignableCasesId));
    }
}
