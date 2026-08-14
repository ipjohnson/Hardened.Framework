using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What happens when the file in <c>AdditionalFiles</c> is not the model the generator expected.
///
/// <para>
/// This class used to cover malformed <i>specifications</i>, because the generator claimed every
/// <c>.yaml</c>, <c>.yml</c> and <c>.json</c> additional file and parsed them itself. It no longer
/// reads yaml at all - <c>Hardened.OpenApi.BuildTask</c> does, and rejecting a bad specification is
/// its job, covered by <c>MalformedSpecTests</c> over there. A spec that will not parse is an error
/// now rather than a warning, because the author named that file as a specification; the old
/// leniency existed for files that were never meant to be specs in the first place.
/// </para>
/// <para>
/// What is left here is narrower and still worth pinning: the generator claims only its own suffix,
/// never throws, and always emits the diagnostic file.
/// </para>
/// </summary>
public class MalformedSpecDiagnosticsTests {

    private const string ModelFile = "petstore.openapi-model.txt";

    private static IEnumerable<Diagnostic> ReadWarnings(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HOAG002");

    /// <summary>
    /// A model file the serializer will not read - truncated on disk, or written by a mismatched
    /// task. Reported, and the build carries on.
    /// </summary>
    [Fact]
    public void ACorruptModelFileReportsHoag002AndDoesNotBreakTheBuild() {
        var result = OpenApiGenerator
            .RunRaw(new Dictionary<string, string> { [ModelFile] = "garbage that is not a model" })
            .AssertNoErrors();

        var warning = Assert.Single(ReadWarnings(result));

        Assert.Contains(ModelFile, warning.GetMessage());
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    /// <summary>
    /// An empty model file is reported separately from a corrupt one: the usual cause is a task that
    /// ran and wrote nothing rather than a model that is wrong.
    /// </summary>
    [Fact]
    public void AnEmptyModelFileReportsHoag002() {
        var result = OpenApiGenerator
            .RunRaw(new Dictionary<string, string> { [ModelFile] = "" })
            .AssertNoErrors();

        var warning = Assert.Single(ReadWarnings(result));

        Assert.Contains("Empty content", warning.GetMessage());
        Assert.Contains(ModelFile, warning.GetMessage());
    }

    /// <summary>
    /// A model written by a future version of the task. The header is checked rather than guessed
    /// at, because a half-understood model generates wrong code instead of an error.
    /// </summary>
    [Fact]
    public void AModelFileFromAnUnknownFormatVersionReportsHoag002() {
        var result = OpenApiGenerator
            .RunRaw(new Dictionary<string, string> {
                [ModelFile] = "#hardened-openapi-model 99\nspec\tFileName=petstore\n"
            })
            .AssertNoErrors();

        Assert.Contains(ReadWarnings(result), warning => warning.GetMessage().Contains("hardened-openapi-model"));
    }

    /// <summary>
    /// The suffix is ours, so nothing else claims it. This is the case the old extension matching got
    /// wrong: a project shipping <c>appsettings.json</c> or a compose file had them handed to the
    /// OpenAPI reader and reported as parse failures.
    /// </summary>
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("docker-compose.yaml")]
    [InlineData("petstore.yaml")]
    [InlineData("notes.txt")]
    public void AnAdditionalFileThatIsNotASpecModelIsIgnored(string fileName) {
        var result = OpenApiGenerator
            .RunRaw(new Dictionary<string, string> { [fileName] = Specs.NotOpenApiJson })
            .AssertNoErrors();

        Assert.Empty(ReadWarnings(result));
    }

    /// <summary>
    /// The diagnostic file is emitted whatever happens and lists every additional file the generator
    /// was handed. <c>Total AdditionalTexts: 0</c> is the first thing to look at when nothing was
    /// generated, so the count has to be there even when the read failed.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileRecordsThePathsAndTheReadFailure() {
        var result = OpenApiGenerator
            .RunRaw(new Dictionary<string, string> { [ModelFile] = "garbage" })
            .AssertNoErrors();

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 1", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 0", diagnosticFile);
        Assert.Contains(ModelFile, diagnosticFile);
        Assert.Contains("Parse errors:", diagnosticFile);
    }

    /// <summary>
    /// The successful case: it reports what it read and lists no errors. Anyone debugging a build
    /// reads this before anything else.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileRecordsASuccessfulReadWithNoErrors() {
        var result = OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors();

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 1", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 1", diagnosticFile);
        Assert.DoesNotContain("Parse errors:", diagnosticFile);
    }

    /// <summary>
    /// A project with no specification at all. The generator still runs, still emits the diagnostic
    /// file, and says the count was zero - which is the answer to "why did nothing generate".
    /// </summary>
    [Fact]
    public void AProjectWithNoAdditionalFilesStillGetsTheDiagnosticFile() {
        var result = OpenApiGenerator.RunRaw(new Dictionary<string, string>()).AssertNoErrors();

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 0", diagnosticFile);
        Assert.Contains("(none)", diagnosticFile);
    }

    /// <summary>
    /// One unreadable model does not take a good one with it.
    /// </summary>
    /// <remarks>
    /// Driven raw, so the task's half of the output is absent and the generated handler has no
    /// record to bind against - hence no compile assertion. What is under test is that an unreadable
    /// model does not stop the generator processing a readable one, and the handler it emits for the
    /// good model is enough to show that.
    /// </remarks>
    [Fact]
    public void AGoodModelStillGeneratesAlongsideABadOne() {
        var goodModel = SpecModelSerializer.Write(
            OpenApiSpecParser.Parse(Specs.Minimal, "petstore", CancellationToken.None)!);

        var result = OpenApiGenerator.RunRaw(new Dictionary<string, string> {
            ["petstore.openapi-model.txt"] = goodModel,
            ["broken.openapi-model.txt"] = "garbage"
        });

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("PetController_ListPets"));
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "HOAG002" && diagnostic.GetMessage().Contains("broken.openapi-model.txt"));
    }

    /// <summary>
    /// The generator must never throw. Roslyn hands a generator exception back on the run result
    /// rather than rethrowing, so a crash on a malformed model would look like a build that simply
    /// generated nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a model")]
    [InlineData("#hardened-openapi-model 1")]
    [InlineData("#hardened-openapi-model 1\nspec")]
    [InlineData("#hardened-openapi-model 1\nnosuchrecord\tKey=value")]
    [InlineData("#hardened-openapi-model 1\nspec\tmalformed-field")]
    [InlineData("#hardened-openapi-model 1\nprop\tName=orphan")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\0")]
    public void NoMalformedModelMakesTheGeneratorThrow(string content) {
        var result = OpenApiGenerator.RunRaw(
            new Dictionary<string, string> { ["candidate.openapi-model.txt"] = content });

        Assert.Empty(result.GeneratorExceptions);
        result.AssertNoErrors();
    }
}
