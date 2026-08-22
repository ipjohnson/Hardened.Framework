using System.Text.Json;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What a module publishing a document it cannot fill is told.
/// </summary>
/// <remarks>
/// <para>
/// The template shipped with <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c> on the host module,
/// which composes the route library rather than declaring routes itself. The document is written
/// from the routes in the same compilation as the attribute, so every code-first application
/// generated from that template served <c>"paths": {}</c>.
/// </para>
/// <para>
/// Nothing failed anywhere: the build was clean, <c>/openapi.json</c> answered 200, and the
/// reference page rendered - as an API with no operations, which is what an API with no operations
/// also looks like. These pin the diagnostic that makes the difference visible.
/// </para>
/// </remarks>
public class OpenApiEmptyDocumentTests {

    private static readonly Type[] Anchors = [typeof(GetAttribute)];

    private const string Marker =
        "[Hardened.Shared.Runtime.Attributes.Enable<" +
        "Hardened.Web.Runtime.OpenApi.OpenApiDocumentPublishing>]";

    /// <param name="controller">
    /// Empty for the module that composes rather than declares - the host's position.
    /// </param>
    private static GeneratorResult Generate(string controller) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    {{Marker}}
                    public partial class TestApplication { }

                    {{controller}}
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors,
            additionalTexts: null,
            buildProperties: null);

    private const string Controller =
        """
        public class UserController {
            [Get("/users/{id}")]
            public string ById(string id) => id;
        }
        """;

    private static Diagnostic? Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id == OpenApiDocumentDiagnostics.EmptyDocumentId);

    /// <summary>
    /// A module that publishes a document and declares no routes is told so.
    /// </summary>
    [Fact]
    public void PublishingFromAModuleWithNoRoutesIsReported() {
        var reported = Reported(Generate(controller: ""));

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Warning, reported!.Severity);
        Assert.Contains("TestApplication", reported.GetMessage());
        Assert.Contains("/openapi.json", reported.GetMessage());
    }

    /// <summary>
    /// The message says where the attribute belongs, because "the document is empty" is a symptom
    /// and the fix is not guessable from it.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheFix() {
        var message = Reported(Generate(controller: ""))!.GetMessage();

        Assert.Contains("same compilation", message);
        Assert.Contains("Enable<OpenApiDocumentPublishing>", message);
    }

    /// <summary>
    /// A module that declares the routes it publishes is not warned. This is the template's
    /// arrangement after the fix, and a diagnostic that fired here would fire on every correct
    /// application.
    /// </summary>
    [Fact]
    public void PublishingBesideTheRoutesReportsNothing() {
        Assert.Null(Reported(Generate(Controller)));
    }

    /// <summary>
    /// The symptom itself, so the diagnostic is pinned against the thing it describes rather than
    /// against its own message.
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData(Controller, 1)]
    public void TheDocumentDescribesOnlyTheRoutesInItsOwnCompilation(string controller, int paths) {
        var source = Generate(controller).GeneratedSources
            .First(pair => pair.Key.Contains("OpenApiDocument")).Value;

        using var document = JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source));

        Assert.Equal(paths, document.RootElement.GetProperty("paths").EnumerateObject().Count());
    }
}
