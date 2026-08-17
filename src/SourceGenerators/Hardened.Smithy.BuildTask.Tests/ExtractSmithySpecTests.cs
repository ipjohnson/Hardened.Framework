using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The task end to end: an AST on disk, through the shared shell, to C# on disk.
/// </summary>
/// <remarks>
/// The parser tests cover the reading. These cover the part that only exists because the shell is
/// shared - that a second front end supplying nothing but a parse gets the emitters, the model file
/// and the diagnostics for free, and under its own diagnostic codes.
/// </remarks>
public class ExtractSmithySpecTests {

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Execute_WritesAModelAndGeneratedSource() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));

        var result = harness.Run("", path);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.True(File.Exists(harness.ModelPathFor("petstore.json")));
        Assert.True(File.Exists(harness.SourcePathFor("petstore.json")));
    }

    /// <summary>
    /// The whole point of the shared shell: a front end that supplies only a parse inherits every
    /// emitter. None of these types is written by anything in Hardened.Smithy.BuildTask.
    /// </summary>
    [Fact]
    public void Execute_EmitsRecordsEnumsAndAServiceInterface() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));
        var result = harness.Run("", path);

        Assert.True(result.Succeeded, result.ErrorText);

        var source = File.ReadAllText(harness.SourcePathFor("petstore.json"));

        Assert.Contains("record Pet", source);
        Assert.Contains("enum PetKind", source);
        Assert.Contains("PetStore", source);
        Assert.Contains("Test.Api", source);
    }

    /// <summary>
    /// Emitted from the neutral model, so the AOT resolver a Smithy-described service gets is the
    /// same one an OpenAPI-described service gets.
    /// </summary>
    [Fact]
    public void Execute_EmitsAJsonTypeInfoResolver() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));
        var result = harness.Run("", path);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Contains("JsonTypeInfo", File.ReadAllText(harness.SourcePathFor("petstore.json")));
    }

    [Fact]
    public void Execute_MissingFileIsReportedUnderThisFrontEndsCode() {
        using var harness = new SmithyTaskHarness();

        var result = harness.Run("", Path.Combine(harness.AstDirectory, "absent.json"));

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HSMT001"), result.ErrorText);
    }

    [Fact]
    public void Execute_UnparseableAstIsReportedUnderThisFrontEndsCode() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("broken.json", "{ not json");

        var result = harness.Run("", path);

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HSMT002"), result.ErrorText);
    }

    /// <summary>
    /// An empty file is the shape of a specific failure - <c>smithy ast</c> writes to stdout and
    /// writes nothing when validation fails, so a redirect leaves an empty file rather than none.
    /// </summary>
    [Fact]
    public void Execute_EmptyAstExplainsTheRedirectFailure() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("empty.json", "");

        var result = harness.Run("", path);

        Assert.False(result.Succeeded);
        Assert.Contains("stdout", result.ErrorText);
    }

    [Fact]
    public void Execute_SelectsTheNamedService() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));

        var result = harness.Run("com.example.petstore#PetStore", path);

        Assert.True(result.Succeeded, result.ErrorText);
    }

    [Fact]
    public void Execute_UnknownServiceShapeIdFailsTheBuild() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));

        var result = harness.Run("com.example#Nope", path);

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HSMT002"), result.ErrorText);
    }

    /// <summary>
    /// Reused deliberately. The file holds a ServiceSpecModel rather than any particular
    /// description, and the source generator matches this one suffix - so sharing it is what lets a
    /// second front end reuse the whole generator half for no code.
    /// </summary>
    [Fact]
    public void Execute_WritesTheModelUnderTheSharedSuffix() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));
        var result = harness.Run("", path);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.EndsWith(".openapi-model.txt", Assert.Single(result.ModelFiles));
    }

    /// <summary>
    /// Rewriting an unchanged file would bump its timestamp, and both the compiler's up-to-date
    /// check and the generator's incremental cache key off that.
    /// </summary>
    [Fact]
    public void Execute_LeavesUnchangedOutputAlone() {
        using var harness = new SmithyTaskHarness();

        var path = harness.WriteAst("petstore.json", Fixture("petstore.json"));

        Assert.True(harness.Run("", path).Succeeded);

        var modelPath = harness.ModelPathFor("petstore.json");
        var stamp = File.GetLastWriteTimeUtc(modelPath);

        Assert.True(harness.Run("", path).Succeeded);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(modelPath));
    }
}
