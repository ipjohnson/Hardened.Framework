using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What happens when the file in <c>AdditionalFiles</c> is not the specification the build expected.
///
/// <para>
/// The generator claims every <c>.yaml</c>, <c>.yml</c> and <c>.json</c> additional file as a
/// candidate, so a project that also ships <c>appsettings.json</c> or a CI configuration hands the
/// generator files that are not OpenAPI documents at all. None of them may take the build down: the
/// contract is an <c>HOAG002</c> warning, a note in <c>_OpenApiDiagnostic.g.cs</c>, and a build that
/// still compiles.
/// </para>
/// </summary>
public class MalformedSpecDiagnosticsTests {

    /// <summary>
    /// Runs the generator over one candidate file and insists the build still compiles — a spec the
    /// generator cannot read is a warning, never a broken build.
    /// </summary>
    private static GeneratorResult RunWith(string content, string fileName) =>
        OpenApiGenerator.Run(content, specFileName: fileName).AssertNoErrors();

    private static IEnumerable<Diagnostic> ParseWarnings(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HOAG002");

    /// <summary>
    /// A document the OpenAPI reader rejects outright. The generator catches it, reports
    /// <c>HOAG002</c>, and lets the build carry on with nothing generated for that file.
    /// </summary>
    [Fact]
    public void AnUnparseableSpecReportsHoag002AndDoesNotBreakTheBuild() {
        var result = RunWith("this is not an openapi document", "broken.yaml");

        var warning = Assert.Single(ParseWarnings(result));

        Assert.Contains("broken.yaml", warning.GetMessage());
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    /// <summary>
    /// An empty file. It is reported separately from an unparseable one, because the usual cause is
    /// a specification that has not been written yet rather than one that is wrong.
    /// </summary>
    [Fact]
    public void AnEmptySpecFileReportsHoag002() {
        var result = RunWith("", "empty.yaml");

        var warning = Assert.Single(ParseWarnings(result));

        Assert.Contains("Empty content", warning.GetMessage());
        Assert.Contains("empty.yaml", warning.GetMessage());
    }

    /// <summary>
    /// A file that is entirely whitespace reads the same as an empty one rather than reaching the
    /// reader.
    /// </summary>
    [Fact]
    public void AWhitespaceOnlySpecFileReportsHoag002() {
        var result = RunWith("   \n  \n", "blank.yaml");

        Assert.Contains(ParseWarnings(result), warning => warning.GetMessage().Contains("blank.yaml"));
    }

    /// <summary>
    /// Valid YAML that is not an OpenAPI document — the case that matters, because a project's CI or
    /// container configuration can land in <c>AdditionalFiles</c> by accident.
    /// </summary>
    [Fact]
    public void ValidYamlThatIsNotAnOpenApiDocumentReportsHoag002() {
        var result = RunWith(Specs.NotOpenApiYaml, "docker-compose.yaml");

        Assert.Contains(ParseWarnings(result),
            warning => warning.GetMessage().Contains("docker-compose.yaml"));
    }

    /// <summary>
    /// Valid JSON that is not an OpenAPI document. <c>appsettings.json</c> is the file most likely to
    /// be handed to the generator by mistake, and it must not fail the build.
    /// </summary>
    [Fact]
    public void ValidJsonThatIsNotAnOpenApiDocumentReportsHoag002() {
        var result = RunWith(Specs.NotOpenApiJson, "appsettings.json");

        Assert.Contains(ParseWarnings(result),
            warning => warning.GetMessage().Contains("appsettings.json"));
    }

    /// <summary>
    /// A file the generator does not claim at all. Anything that is not <c>.yaml</c>, <c>.yml</c> or
    /// <c>.json</c> is left alone, so a template or a text asset in <c>AdditionalFiles</c> produces
    /// no warning.
    /// </summary>
    [Fact]
    public void AnAdditionalFileWithAnUnclaimedExtensionIsIgnored() {
        var result = RunWith("not a spec at all", "notes.txt");

        Assert.Empty(ParseWarnings(result));
    }

    /// <summary>
    /// The diagnostic file is emitted whatever happens, and lists every additional file the generator
    /// was handed. <c>Total AdditionalTexts: 0</c> is the first thing to look at when nothing was
    /// generated, so the count has to be there even when the parse failed.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileRecordsThePathsAndTheParseFailure() {
        var result = RunWith(Specs.NotOpenApiJson, "appsettings.json");

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 1", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 0", diagnosticFile);
        Assert.Contains("appsettings.json", diagnosticFile);
        Assert.Contains("Parse errors:", diagnosticFile);
    }

    /// <summary>
    /// The successful case of the same file: it reports what it parsed, and lists no errors. Anyone
    /// debugging a build reads this before anything else.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileRecordsASuccessfulParseWithNoErrors() {
        var result = RunWith(Specs.Minimal, "petstore.yaml");

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 1", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 1", diagnosticFile);
        Assert.DoesNotContain("Parse errors:", diagnosticFile);
    }

    /// <summary>
    /// A project with no specification at all. The generator still runs, still emits the diagnostic
    /// file, and says the count was zero — which is the answer to "why did nothing generate".
    /// </summary>
    [Fact]
    public void AProjectWithNoAdditionalFilesStillGetsTheDiagnosticFile() {
        var result = OpenApiGenerator.Run(
                new Dictionary<string, string>(),
                OpenApiGenerator.MinimalEntryPoint)
            .AssertNoErrors();

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 0", diagnosticFile);
        Assert.Contains("(none)", diagnosticFile);
    }

    /// <summary>
    /// One bad specification does not take a good one with it. The build is far more likely to have
    /// picked up a stray configuration file than to have a wrong spec, so a project's real
    /// specification has to keep generating.
    /// </summary>
    [Fact]
    public void AGoodSpecStillGeneratesAlongsideABadOne() {
        var result = OpenApiGenerator.Run(
                new Dictionary<string, string> {
                    ["petstore.yaml"] = Specs.Minimal,
                    ["appsettings.json"] = Specs.NotOpenApiJson
                },
                OpenApiGenerator.MinimalEntryPoint)
            .AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("petstore.Pet.g.cs"));
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "HOAG002" && diagnostic.GetMessage().Contains("appsettings.json"));

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 2", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 1", diagnosticFile);
    }

    /// <summary>
    /// The generator must never throw. Roslyn hands a generator exception back on the run result
    /// rather than rethrowing, so a crash on a malformed document would look like a build that simply
    /// generated nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an openapi document")]
    [InlineData("openapi: 3.0.0")]
    [InlineData("openapi: \"3.0.0\"\ninfo: {}\npaths:")]
    [InlineData("openapi: \"3.0.0\"\ninfo: { title: T, version: \"1.0\" }\npaths:\n  /a:\n    get: {}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\0\u0001\u0002")]
    public void NoMalformedDocumentMakesTheGeneratorThrow(string content) {
        var result = OpenApiGenerator.Run(content, specFileName: "candidate.yaml");

        Assert.Empty(result.GeneratorExceptions);
        result.AssertNoErrors();
    }
}
