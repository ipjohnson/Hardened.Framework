using Microsoft.Build.Framework;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The task that runs the Smithy CLI, exercised against a stand-in for it.
/// </summary>
/// <remarks>
/// <para>
/// A fake CLI rather than the real one, because these cover what happens when the tool is absent,
/// the wrong version, or fails - none of which the real CLI can be asked to do on demand, and all of
/// which are the paths a person actually meets. The real CLI is covered where it belongs: the
/// integration fixture builds a <c>.smithy</c> model through it end to end.
/// </para>
/// <para>
/// The stand-in is written per-platform rather than skipped on Windows. CI fails the build on any
/// skipped test, on the grounds that a skip is a test the suite is not running.
/// </para>
/// </remarks>
public class GenerateSmithyAstTests : IDisposable {

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hardened-smithy-cli-tests", Guid.NewGuid().ToString("n"));

    public GenerateSmithyAstTests() => Directory.CreateDirectory(_root);

    public void Dispose() {
        try {
            Directory.Delete(_root, recursive: true);
        } catch (IOException) {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>
    /// A stand-in CLI: answers <c>--version</c>, and otherwise writes <paramref name="output"/> to
    /// stdout and exits with <paramref name="exitCode"/>.
    /// </summary>
    private string FakeCli(string version, string output = "", int exitCode = 0, string error = "") {
        if (OperatingSystem.IsWindows()) {
            var batch = Path.Combine(_root, "smithy.cmd");

            File.WriteAllText(batch,
                "@echo off\r\n" +
                $"if \"%1\"==\"--version\" (echo {version}& exit /b 0)\r\n" +
                (output.Length > 0 ? $"echo {output}\r\n" : "") +
                (error.Length > 0 ? $"echo {error} 1>&2\r\n" : "") +
                $"exit /b {exitCode}\r\n");

            return batch;
        }

        var script = Path.Combine(_root, "smithy");

        File.WriteAllText(script,
            "#!/bin/sh\n" +
            $"if [ \"$1\" = \"--version\" ]; then echo '{version}'; exit 0; fi\n" +
            (output.Length > 0 ? $"printf '%s' '{output}'\n" : "") +
            (error.Length > 0 ? $"echo '{error}' 1>&2\n" : "") +
            $"exit {exitCode}\n");

        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return script;
    }

    private string Model() {
        var path = Path.Combine(_root, "model.smithy");

        File.WriteAllText(path, "$version: \"2\"\nnamespace com.example\n");

        return path;
    }

    private (bool Result, SmithyTaskHarness.RecordingBuildEngine Engine, string Output) Run(
        string? toolPath, string expectedVersion = "", bool pinVersion = true) {
        var engine = new SmithyTaskHarness.RecordingBuildEngine();
        var output = Path.Combine(_root, "out", "ast.json");

        var task = new GenerateSmithyAst {
            BuildEngine = engine,
            Models = new ITaskItem[] { new Microsoft.Build.Utilities.TaskItem(Model()) },
            OutputPath = output,
            ToolPath = toolPath ?? "",
            ExpectedVersion = expectedVersion,
            PinVersion = pinVersion
        };

        return (task.Execute(), engine, output);
    }

    private static bool HasError(SmithyTaskHarness.RecordingBuildEngine engine, string code) =>
        engine.Errors.Any(error => error.Code == code);

    /// <summary>
    /// Named rather than thrown, and the message says both ways out - install the CLI, or commit an
    /// AST and skip it entirely.
    /// </summary>
    [Fact]
    public void Execute_ReportsAMissingCli() {
        var (result, engine, _) = Run(Path.Combine(_root, "no-such-smithy"));

        Assert.False(result);
        Assert.True(HasError(engine, "HSMT010"),
            string.Join("\n", engine.Errors.Select(e => e.Message)));
    }

    /// <summary>
    /// Where the pin is enforced, a different CLI stops the build: it can produce a different AST
    /// from identical sources, and that is not something to discover downstream of a publish.
    /// </summary>
    [Fact]
    public void Execute_RefusesACliThatIsNotThePinnedVersion() {
        var (result, engine, output) = Run(FakeCli("1.56.0"), expectedVersion: "1.73.0");

        Assert.False(result);
        Assert.True(HasError(engine, "HSMT011"));
        Assert.False(File.Exists(output));

        var message = engine.Errors.Single(e => e.Code == "HSMT011").Message;

        Assert.Contains("1.56.0", message);
        Assert.Contains("1.73.0", message);
    }

    /// <summary>
    /// And where it is not - a developer machine - the same mismatch is a warning and the build
    /// carries on.
    /// </summary>
    /// <remarks>
    /// The pin belongs on the build that produces artefacts other people consume. Requiring an exact
    /// CLI locally means every developer has to chase a version before the repo builds at all, and
    /// "I pulled and it does not build" is not answered by "install this specific tool first". What
    /// CI publishes is still produced by exactly one version, so nothing about reproducibility
    /// changes.
    /// </remarks>
    [Fact]
    public void Execute_WarnsButBuildsWhenTheVersionIsNotPinned() {
        var (result, engine, output) = Run(
            FakeCli("1.56.0", output: "{\"smithy\":\"2.0\",\"shapes\":{}}"),
            expectedVersion: "1.73.0",
            pinVersion: false);

        Assert.True(result, string.Join("\n", engine.Errors.Select(error => error.Message)));
        Assert.False(HasError(engine, "HSMT011"));
        Assert.True(File.Exists(output));

        var warning = engine.Warnings.Single(entry => entry.Code == "HSMT011").Message;

        Assert.Contains("1.56.0", warning);
        Assert.Contains("1.73.0", warning);
    }

    [Fact]
    public void Execute_AcceptsTheCliWhenTheVersionMatches() {
        var (result, engine, output) = Run(
            FakeCli("1.73.0", output: "{\"smithy\":\"2.0\",\"shapes\":{}}"), expectedVersion: "1.73.0");

        Assert.True(result, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Contains("\"smithy\"", File.ReadAllText(output));
    }

    [Fact]
    public void Execute_AcceptsAnyVersionWhenNothingIsPinned() {
        var (result, engine, _) = Run(FakeCli("0.1.2", output: "{\"shapes\":{}}"));

        Assert.True(result, string.Join("\n", engine.Errors.Select(e => e.Message)));
    }

    /// <summary>
    /// The CLI's own diagnostics name the .smithy file and line, so they are passed through rather
    /// than summarised.
    /// </summary>
    [Fact]
    public void Execute_ReportsWhatTheCliSaidWhenItFails() {
        var (result, engine, output) = Run(FakeCli("1.73.0", exitCode: 1, error: "Model.UnresolvedShape"));

        Assert.False(result);
        Assert.True(HasError(engine, "HSMT012"));
        Assert.Contains("Model.UnresolvedShape", engine.Errors.Single(e => e.Code == "HSMT012").Message);
        Assert.False(File.Exists(output));
    }

    /// <summary>
    /// The failure that made this worth writing carefully: a shell redirect leaves a zero-byte file
    /// behind, which the next up-to-date check would accept as a model and the reader would then
    /// report as broken JSON at position zero.
    /// </summary>
    [Fact]
    public void Execute_LeavesNoOutputBehindWhenTheCliFails() {
        var (result, _, output) = Run(FakeCli("1.73.0", exitCode: 2));

        Assert.False(result);
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp"));
    }

    /// <summary>
    /// A CLI that exits cleanly and emits nothing is a failure, not an empty model.
    /// </summary>
    [Fact]
    public void Execute_TreatsASilentSuccessAsAFailure() {
        var (result, engine, output) = Run(FakeCli("1.73.0"));

        Assert.False(result);
        Assert.True(HasError(engine, "HSMT012"));
        Assert.False(File.Exists(output));
    }

    /// <summary>
    /// Rewriting an identical AST would bump its timestamp, and the extract task and the compiler
    /// both key off that - so editing a comment in a model would rebuild the whole compilation.
    /// </summary>
    [Fact]
    public void Execute_LeavesAnUnchangedAstUntouched() {
        var cli = FakeCli("1.73.0", output: "{\"smithy\":\"2.0\",\"shapes\":{}}");

        var first = Run(cli, expectedVersion: "1.73.0");

        Assert.True(first.Result);

        var stamp = File.GetLastWriteTimeUtc(first.Output);

        var second = Run(cli, expectedVersion: "1.73.0");

        Assert.True(second.Result);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(second.Output));
    }
}
