using Hardened.Idl.BuildTask;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The warnings a run keeps, and what a build that skips the task reports from them.
/// </summary>
/// <remarks>
/// The 0.41 trial's C-21. MSBuild skips the extract target while its outputs are newer than the
/// description, and a skipped task reports nothing, so a warning appeared on one build and never
/// again while the description was unchanged.
/// </remarks>
public class WarningReplayTests
{
    private const string Spec = """
        openapi: "3.1.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              responses:
                '200': { description: pets }
        """;

    [Fact]
    public void AWarningIsReportedAgainByABuildThatSkipsTheTask()
    {
        using var harness = new TaskHarness();
        var path = harness.WriteSpec("pets.yaml", Spec);

        var result = harness.RunWithResponseModel("Standard", path);

        Assert.True(result.Ran);

        var reported = Assert.Single(result.Warnings, warning => warning.Code == "HOAT026");
        var replayed = Assert.Single(harness.Replay());

        Assert.Equal(reported.Code, replayed.Code);
        Assert.Equal(reported.Message, replayed.Message);
    }

    /// <summary>
    /// A run with nothing to report removes the file, so the targets have nothing to replay.
    /// </summary>
    [Fact]
    public void ARunWithNoWarningLeavesNothingToReplay()
    {
        using var harness = new TaskHarness();
        var path = harness.WriteSpec("pets.yaml", Spec);

        harness.RunWithResponseModel("Standard", path);
        harness.Run(path);

        Assert.False(File.Exists(harness.WarningsFile));
        Assert.Empty(harness.Replay());
    }

    /// <summary>A message keeps its tabs, line breaks and backslashes through the file.</summary>
    [Fact]
    public void AWarningSurvivesTheFileWhateverItHolds()
    {
        using var harness = new TaskHarness();
        var message = "a\ttab, a\nbreak, a \\ backslash and a \\n that is text";

        SpecWarnings.Write(
            harness.WarningsFile,
            [
                new SpecWarning("HOAT006", null, message),
                new SpecWarning("HOAT008", "pets.yaml", "b"),
            ]
        );

        var read = SpecWarnings.Read(harness.WarningsFile).ToList();

        Assert.Equal(2, read.Count);
        Assert.Equal(message, read[0].Message);
        Assert.Null(read[0].File);
        Assert.Equal("pets.yaml", read[1].File);
    }
}
