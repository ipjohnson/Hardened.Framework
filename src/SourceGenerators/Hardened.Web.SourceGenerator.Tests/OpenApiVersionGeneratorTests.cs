using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// <c>&lt;HardenedOpenApiVersion&gt;</c> driven through the generator, rather than through
/// <c>OpenApiVersionFacts</c> alone.
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenApiVersionTests</c> covers the parsing. What these cover is the half that was actually
/// broken for the sibling property: a generator sees an MSBuild property only if something declares
/// it <c>CompilerVisibleProperty</c>, and <c>&lt;HardenedAmbiguousRoutes&gt;</c> was read for months
/// while the lookup always missed, because this package shipped no build assets. A unit test over
/// the parser would have passed throughout.
/// </para>
/// <para>
/// So each of these sets the property the way a consumer would and asserts on what came out the
/// other end - the document, or the diagnostic.
/// </para>
/// </remarks>
public class OpenApiVersionGeneratorTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
    ];

    private const string Handlers =
        """
            [Get("/users/{id}")]
            public string ById(string id) => id;
        """;

    private const string StreamingHandler =
        """
            [Get("/users/{id}")]
            public string ById(string id) => id;

            [Get("/feed")]
            public async System.Collections.Generic.IAsyncEnumerable<string> Feed() {
                await System.Threading.Tasks.Task.Yield();

                yield return "one";
            }
        """;

    private static GeneratorResult Generate(string handlers, string? version) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    [Hardened.Shared.Runtime.Attributes.Enable<Hardened.Web.Runtime.OpenApi.HardenedOpenApiDocument>]
                    public partial class TestApplication { }

                    public class UserController {
                    {{handlers}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors,
            additionalTexts: null,
            buildProperties: version == null
                ? null
                : new Dictionary<string, string> { [OpenApiVersionFacts.PropertyName] = version });

    /// <summary>The <c>openapi</c> field of the document the generator wrote.</summary>
    private static string? DeclaredVersion(GeneratorResult result) {
        var source = result.GeneratedSources
            .First(pair => pair.Key.Contains("OpenApiDocument")).Value;

        // The document is emitted gzipped; take it back to JSON the way CI's extractor does.
        using var document = JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source));

        return document.RootElement.GetProperty("openapi").GetString();
    }

    private static Diagnostic? Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.FirstOrDefault(diagnostic => diagnostic.Id == id);

    /// <summary>
    /// Unset emits the default, and the default is 3.2.0.
    /// </summary>
    [Fact]
    public void AnUnsetPropertyEmitsTheDefault() {
        Assert.Equal("3.2.0", DeclaredVersion(Generate(Handlers, null)));
    }

    /// <summary>
    /// The property reaches the generator and changes the document.
    /// </summary>
    /// <remarks>
    /// The assertion that would have caught <c>&lt;HardenedAmbiguousRoutes&gt;</c> being invisible:
    /// it is about the value arriving, not about the parser being right.
    /// </remarks>
    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.1.0")]
    [InlineData("3.2.0")]
    public void ASetPropertyReachesTheDocument(string version) {
        Assert.Equal(version, DeclaredVersion(Generate(Handlers, version)));
    }

    /// <summary>
    /// An unrecognised value fails the build rather than falling back.
    /// </summary>
    /// <remarks>
    /// <c>3.9.9</c> rather than something obviously wrong, because the dangerous case is a value
    /// that looks like a version. Falling back to the default here would emit 3.2.0 to a build that
    /// asked for something else and say nothing, and the property is set precisely by people who
    /// cannot afford that.
    /// </remarks>
    [Fact]
    public void AnUnrecognisedVersionIsABuildError() {
        var reported = Reported(
            Generate(Handlers, "3.9.9"), OpenApiVersionDiagnostics.UnknownVersionId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("3.9.9", reported.GetMessage());
        Assert.Contains("3.2.0", reported.GetMessage());
    }

    [Fact]
    public void ARecognisedVersionReportsNothing() {
        Assert.Null(
            Reported(Generate(Handlers, "3.0.0"), OpenApiVersionDiagnostics.UnknownVersionId));
    }

    /// <summary>
    /// A streamed handler under a version with no <c>itemSchema</c> is named.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: the application still builds and still streams correctly,
    /// and someone pinned to 3.0 for a reader that needs it has made a trade. What they must not do
    /// is believe the document describes the operation.
    /// </remarks>
    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.1.0")]
    public void AStreamedHandlerIsReportedWhenItCannotBeDescribed(string version) {
        var reported = Reported(
            Generate(StreamingHandler, version),
            OpenApiVersionDiagnostics.StreamNeedsItemSchemaId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Warning, reported!.Severity);
        Assert.Contains("Feed", reported.GetMessage());
        Assert.Contains(version, reported.GetMessage());
    }

    /// <summary>At 3.2 there is nothing to report, because the document can say it.</summary>
    [Fact]
    public void AStreamedHandlerIsNotReportedAtThreeTwo() {
        Assert.Null(
            Reported(
                Generate(StreamingHandler, "3.2.0"),
                OpenApiVersionDiagnostics.StreamNeedsItemSchemaId));
    }

    /// <summary>
    /// A handler that streams nothing is never reported, whatever the version.
    /// </summary>
    /// <remarks>
    /// The diagnostic keys on <c>IsAsyncEnumerable</c>, and a warning on every operation of every
    /// 3.0 application would make the property unusable.
    /// </remarks>
    [Fact]
    public void ANonStreamingApplicationIsNeverReported() {
        Assert.Null(
            Reported(
                Generate(Handlers, "3.0.0"),
                OpenApiVersionDiagnostics.StreamNeedsItemSchemaId));
    }
}
