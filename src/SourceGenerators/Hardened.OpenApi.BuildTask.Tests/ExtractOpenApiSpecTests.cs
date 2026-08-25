using Hardened.Generation;
using Hardened.OpenApi.SourceGenerator;
using Xunit;
using Hardened.Idl;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The task that replaced the generator's yaml reading.
/// </summary>
/// <remarks>
/// <para>
/// The leniency this used to have is gone on purpose. The generator claimed every <c>.yaml</c>,
/// <c>.yml</c> and <c>.json</c> additional file, so a project shipping <c>appsettings.json</c> had
/// it handed to the OpenAPI reader; failing the build over that would have been indefensible, and
/// the contract was a warning. Specifications are named explicitly now, with
/// <c>&lt;HardenedOpenApiSpec/&gt;</c>, so a file that will not parse is a file the author called a
/// specification - and an error.
/// </para>
/// </remarks>
public class ExtractOpenApiSpecTests {

    [Fact]
    public void Execute_WritesAModelBesideEachSpec() {
        using var harness = new TaskHarness();
        var spec = harness.WriteSpec("petstore.yaml", Specs.Minimal);

        var result = harness.Run(spec);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.True(File.Exists(harness.ModelPathFor("petstore.yaml")));
        Assert.Equal(new[] { harness.ModelPathFor("petstore.yaml") }, result.ModelFiles);
    }

    [Fact]
    public void Execute_WritesAModelTheGeneratorCanRead() {
        using var harness = new TaskHarness();

        harness.Run(harness.WriteSpec("petstore.yaml", Specs.Minimal));

        var model = SpecModelSerializer.Read(File.ReadAllText(harness.ModelPathFor("petstore.yaml")));

        Assert.Equal("petstore", model.FileName);
        Assert.Contains(model.Schemas, schema => schema.Name == "Pet");
    }

    [Fact]
    public void Execute_HandlesSeveralSpecsInOneCall() {
        using var harness = new TaskHarness();

        var result = harness.Run(
            harness.WriteSpec("pets.yaml", Specs.Minimal),
            harness.WriteSpec("stores.yaml", Specs.Minimal));

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Equal(2, result.ModelFiles.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("this is not an openapi document")]
    [InlineData("[1, 2, 3]")]
    [InlineData(Specs.NotOpenApiYaml)]
    [InlineData(Specs.NotOpenApiJson)]
    public void Execute_FailsOnASpecItCannotParse(string content) {
        using var harness = new TaskHarness();

        var result = harness.Run(harness.WriteSpec("broken.yaml", content));

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HOAT002"), result.ErrorText);
    }

    /// <summary>
    /// The error names the spec, not the target that ran the task. The author edits the yaml, and a
    /// build error pointing at an MSBuild file instead is noise.
    /// </summary>
    [Fact]
    public void Execute_ReportsTheSpecPathRatherThanTheTask() {
        using var harness = new TaskHarness();
        var spec = harness.WriteSpec("broken.yaml", "not a document");

        var result = harness.Run(spec);

        Assert.Contains(result.Errors, error => error.File == spec);
    }

    [Fact]
    public void Execute_FailsWhenASpecIsMissing() {
        using var harness = new TaskHarness();

        var result = harness.Run(Path.Combine(harness.SpecDirectory, "absent.yaml"));

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HOAT001"), result.ErrorText);
    }

    /// <summary>
    /// One bad specification stops the build, but every other spec is still read first, so a single
    /// run reports all of them rather than one per build.
    /// </summary>
    [Fact]
    public void Execute_ReportsEveryBadSpecInOneRun() {
        using var harness = new TaskHarness();

        var result = harness.Run(
            harness.WriteSpec("broken-one.yaml", "not a document"),
            harness.WriteSpec("broken-two.yaml", "also not a document"));

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Errors.Count);
    }

    /// <summary>
    /// Rewriting an unchanged model would move its timestamp, and the generator treats every change
    /// to an AdditionalFile as a reason to re-run - so an untouched spec would invalidate the whole
    /// generator on every build.
    /// </summary>
    [Fact]
    public void Execute_LeavesAnUnchangedModelUntouched() {
        using var harness = new TaskHarness();
        var spec = harness.WriteSpec("petstore.yaml", Specs.Minimal);
        var modelPath = harness.ModelPathFor("petstore.yaml");

        harness.Run(spec);
        var firstWrite = File.GetLastWriteTimeUtc(modelPath);

        File.SetLastWriteTimeUtc(modelPath, firstWrite.AddDays(-1));
        var backdated = File.GetLastWriteTimeUtc(modelPath);

        harness.Run(spec);

        Assert.Equal(backdated, File.GetLastWriteTimeUtc(modelPath));
    }

    [Fact]
    public void Execute_RewritesAModelWhenTheSpecChanges() {
        using var harness = new TaskHarness();
        var spec = harness.WriteSpec("petstore.yaml", Specs.Minimal);
        var modelPath = harness.ModelPathFor("petstore.yaml");

        harness.Run(spec);
        var first = File.ReadAllText(modelPath);

        harness.WriteSpec("petstore.yaml", Specs.Minimal.Replace("Pet:", "Animal:").Replace("/Pet'", "/Animal'"));
        harness.Run(spec);

        Assert.NotEqual(first, File.ReadAllText(modelPath));
    }

    [Fact]
    public void Execute_CreatesTheOutputDirectoryIfItIsAbsent() {
        using var harness = new TaskHarness();
        Directory.Delete(harness.OutputDirectory);

        var result = harness.Run(harness.WriteSpec("petstore.yaml", Specs.Minimal));

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.True(File.Exists(harness.ModelPathFor("petstore.yaml")));
    }
}
