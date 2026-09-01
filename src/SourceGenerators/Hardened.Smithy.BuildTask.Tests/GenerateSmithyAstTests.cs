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
    /// stdout and exits with <paramref name="exitCode"/>. <paramref name="report"/> reaches stderr
    /// through a file, because the CLI's multi-line validation report is nothing <c>echo</c> can
    /// carry on either platform.
    /// </summary>
    private string FakeCli(
        string version, string output = "", int exitCode = 0, string error = "",
        string report = "") {
        var reportEmit = "";

        if (report.Length > 0) {
            var reportPath = Path.Combine(_root, "report.txt");

            File.WriteAllText(reportPath, report);

            reportEmit = OperatingSystem.IsWindows()
                ? $"type \"{reportPath}\" 1>&2\r\n"
                : $"cat '{reportPath}' 1>&2\n";
        }

        if (OperatingSystem.IsWindows()) {
            var batch = Path.Combine(_root, "smithy.cmd");

            File.WriteAllText(batch,
                "@echo off\r\n" +
                $"if \"%1\"==\"--version\" (echo {version}& exit /b 0)\r\n" +
                (output.Length > 0 ? $"echo {output}\r\n" : "") +
                (error.Length > 0 ? $"echo {error} 1>&2\r\n" : "") +
                reportEmit +
                $"exit /b {exitCode}\r\n");

            return batch;
        }

        var script = Path.Combine(_root, "smithy");

        File.WriteAllText(script,
            "#!/bin/sh\n" +
            $"if [ \"$1\" = \"--version\" ]; then echo '{version}'; exit 0; fi\n" +
            (output.Length > 0 ? $"printf '%s' '{output}'\n" : "") +
            (error.Length > 0 ? $"echo '{error}' 1>&2\n" : "") +
            reportEmit +
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
    /// Stderr that is not the validation report - a crash, a launcher complaint - is passed
    /// through whole rather than lost, against the first model because there is nothing better.
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
    /// What the CLI prints when it refuses a model: one banner per finding, each naming its file,
    /// line and column. The excerpt and the count line are the report's own layout.
    /// </summary>
    private const string Report =
        """

        ──  ERROR  ────────────────────────────────────────────── Target.UnresolvedShape
        Shape: probe#Svc
        File:  models/bad.smithy:4:1

        4| service Svc {
         | ^

        service shape has an `operation` relationship to an unresolved shape
        `probe#MissingOp`


        ──  DANGER  ───────────────────────────────────────────── SyntacticShapeIdTarget
        File:  models/bad.smithy:6:18

        6|     operations: [MissingOp]
         |                  ^

        Syntactic shape ID `MissingOp` does not resolve to a valid shape ID:
        `probe#MissingOp`. Did you mean to quote this string? Are you missing a model
        file?

        FAILURE: Validated 242 shapes (ERROR: 1, DANGER: 1)
        """;

    /// <summary>
    /// The CLI names the file and line of every finding, and pinning the whole report to the
    /// first model at 0,0 threw that away - a five-file model with one bad line pointed the
    /// author at the wrong file.
    /// </summary>
    [Fact]
    public void Execute_ReportsOneErrorPerFindingWhereTheCliPlacedIt() {
        var (result, engine, _) = Run(FakeCli("1.73.0", exitCode: 1, report: Report));

        Assert.False(result);
        Assert.Equal(2, engine.Errors.Count);
        Assert.All(engine.Errors, entry => Assert.Equal("HSMT012", entry.Code));

        var unresolved = engine.Errors[0];

        Assert.Equal(Path.GetFullPath(Path.Combine("models", "bad.smithy")), unresolved.File);
        Assert.Equal(4, unresolved.LineNumber);
        Assert.Equal(1, unresolved.ColumnNumber);
        Assert.StartsWith("Target.UnresolvedShape on probe#Svc:", unresolved.Message);
        Assert.Contains("unresolved shape", unresolved.Message);

        var syntactic = engine.Errors[1];

        Assert.Equal(6, syntactic.LineNumber);
        Assert.StartsWith("SyntacticShapeIdTarget:", syntactic.Message);
        Assert.DoesNotContain("FAILURE", syntactic.Message);
    }

    /// <summary>The same attribution for what the CLI got away with on a run it exited cleanly from.</summary>
    [Fact]
    public void Execute_AttributesWarningsTheCliPrintedOnASuccessfulRun() {
        var (result, engine, _) = Run(FakeCli(
            "1.73.0",
            output: "{\"smithy\":\"2.0\",\"shapes\":{}}",
            report: "──  WARNING  ──── HttpMethodSemantics\n" +
                    "File:  models/ok.smithy:5:1\n" +
                    "\n" +
                    "POST on a @readonly operation\n"));

        Assert.True(result, string.Join("\n", engine.Errors.Select(e => e.Message)));

        var warning = Assert.Single(engine.Warnings);

        Assert.Equal("HSMT013", warning.Code);
        Assert.Equal(Path.GetFullPath(Path.Combine("models", "ok.smithy")), warning.File);
        Assert.Equal(5, warning.LineNumber);
        Assert.StartsWith("HttpMethodSemantics:", warning.Message);
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
    /// A CLI that exits cleanly and emits nothing is a failure, not an empty model. Its own code
    /// rather than HSMT012, because that one means the CLI refused the model and this one's fix is
    /// not in any .smithy file.
    /// </summary>
    [Fact]
    public void Execute_TreatsASilentSuccessAsAFailure() {
        var (result, engine, output) = Run(FakeCli("1.73.0"));

        Assert.False(result);
        Assert.True(HasError(engine, "HSMT014"),
            string.Join("\n", engine.Errors.Select(e => e.Code)));
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
