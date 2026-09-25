using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// <c>x-hardened-validation</c> on a code-first document, from a <c>[ValidationMode]</c> on the
/// operation, its class or the module.
/// </summary>
/// <remarks>
/// Published so the exported document round-trips into a service that reports its failures the
/// same way, as <c>x-hardened-timeout</c> is for a deadline.
/// </remarks>
public class ValidationModeDocumentTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(ValidationModeAttribute), // Hardened.Requests.Runtime
        typeof(IValidatorFor<object>), // ValidationModules.Runtime
        typeof(EnableAttribute<>), // Hardened.Shared.Runtime
        typeof(OpenApiDocumentPublishing), // the marker
    ];

    private static JsonElement Operation(string moduleAttribute, string controller, string path)
    {
        var source = $$"""
            using Hardened.Requests.Runtime.Validation;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.OpenApi;
            using ValidationModules;

            namespace TestApp;

            [HardenedModule]
            [Enable<OpenApiDocumentPublishing>]
            {{moduleAttribute}}
            public partial class Application { }

            {{controller}}
            """;

        var result = GeneratorTestHarness
            .Run(
                new Dictionary<string, string> { ["Test.cs"] = source },
                new IIncrementalGenerator[]
                {
                    new WebLibrarySourceGenerator(),
                    ValidationModulesPackage.Generator(),
                },
                Anchors,
                buildProperties: ValidationModulesPackage.BuildProperties
            )
            .AssertNoErrors();

        var match = Regex.Match(
            result.SourceContaining("OpenApiDocument"),
            @"new byte\[\]\s*\{(.*?)\}\s*;",
            RegexOptions.Singleline
        );

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match
            .Groups[1]
            .Value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Select(byte.Parse)
            .ToArray();

        using var compressed = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument
            .Parse(inflated.ToArray())
            .RootElement.GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .Clone();
    }

    private static string? Published(JsonElement operation) =>
        operation.TryGetProperty("x-hardened-validation", out var mode) ? mode.GetString() : null;

    private const string Orders = """
        public class OrderController {
            [Post("/orders")]
            [ValidationMode(ValidationStopMode.StopOnFirstError)]
            public string Place(string order) => order;
        }
        """;

    [Fact]
    public void TheOperationsOwnDeclarationIsPublished()
    {
        Assert.Equal("stop-on-first-error", Published(Operation("", Orders, "/orders")));
    }

    [Fact]
    public void TheClassesDeclarationReachesItsOperations()
    {
        const string controller = """
            [ValidationMode(ValidationStopMode.StopOnFirstError)]
            public class OrderController {
                [Post("/orders")]
                public string Place(string order) => order;
            }
            """;

        Assert.Equal("stop-on-first-error", Published(Operation("", controller, "/orders")));
    }

    /// <summary>
    /// The runtime merges a module's declaration into every handler that declares none, so the
    /// document says the same of each operation.
    /// </summary>
    [Fact]
    public void TheModulesDeclarationReachesAnOperationThatDeclaresNone()
    {
        const string controller = """
            public class OrderController {
                [Post("/orders")]
                public string Place(string order) => order;
            }
            """;

        Assert.Equal(
            "stop-on-first-error",
            Published(
                Operation(
                    "[ValidationMode(ValidationStopMode.StopOnFirstError)]",
                    controller,
                    "/orders"
                )
            )
        );
    }

    [Fact]
    public void AnOperationsOwnDeclarationBeatsTheModules()
    {
        const string controller = """
            public class OrderController {
                [Post("/orders")]
                [ValidationMode(ValidationStopMode.CollectAll)]
                public string Place(string order) => order;
            }
            """;

        Assert.Equal(
            "collect-all",
            Published(
                Operation(
                    "[ValidationMode(ValidationStopMode.StopOnFirstError)]",
                    controller,
                    "/orders"
                )
            )
        );
    }

    [Fact]
    public void AnOperationUnderNoDeclarationPublishesNone()
    {
        const string controller = """
            public class OrderController {
                [Post("/orders")]
                public string Place(string order) => order;
            }
            """;

        Assert.Null(Published(Operation("", controller, "/orders")));
    }
}
