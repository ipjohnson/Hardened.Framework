using System.Diagnostics;
using System.Text;
using Microsoft.Build.Framework;

namespace Hardened.Smithy.BuildTask;

/// <summary>
/// Runs the Smithy CLI over a model and writes the JSON AST the extract task reads.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a project can commit <c>.smithy</c> files rather than a generated artifact. The
/// AST lands in <c>obj/</c>, which means it is never seen, never reviewed and never able to drift
/// from the model it came from - the failure the committed-AST arrangement could not rule out,
/// because nothing checked that the two agreed.
/// </para>
/// <para>
/// <b>The extract task is unchanged and still only ever eats an AST.</b> That separation is what
/// keeps a hermetic build available: a project that points <c>$(HardenedSmithyAst)</c> at a
/// committed <c>.json</c> resolves no tool, downloads nothing, and never starts a process. This task
/// is the other way in, not a replacement for it.
/// </para>
/// <para>
/// <b>Not a ToolTask.</b> <c>smithy ast</c> writes the model to stdout and creates no file, and
/// ToolTask's stdout handling logs every line as an MSBuild message - for a pretty-printed AST that
/// is tens of thousands of log lines. Standard output is redirected into a file here and only
/// standard error reaches the log.
/// </para>
/// </remarks>
public sealed class GenerateSmithyAst : Microsoft.Build.Utilities.Task {

    /// <summary>The <c>.smithy</c> files, or directories holding them, that form one model.</summary>
    /// <remarks>
    /// All of them, in one invocation, producing one AST. A Smithy model is routinely several files
    /// that only resolve together - a namespace split across them, a shape in one referenced from
    /// another - so running the CLI per file would fail on exactly the models that need it most.
    /// </remarks>
    [Required]
    public ITaskItem[] Models { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>Where to write the AST.</summary>
    [Required]
    public string OutputPath { get; set; } = "";

    /// <summary>An explicit CLI path, which wins over anything on PATH.</summary>
    public string ToolPath { get; set; } = "";

    /// <summary>
    /// The CLI version this build is pinned to. Empty to accept whatever is installed.
    /// </summary>
    /// <remarks>
    /// A build whose output depends on an unpinned external tool is not reproducible: two machines
    /// on different CLI versions can produce different ASTs, and therefore different C#, from
    /// identical sources. Worse, validation strictness moves between versions, so a model that
    /// builds today can fail tomorrow because someone upgraded. Pinning makes that a deliberate
    /// change rather than a surprise.
    /// </remarks>
    public string ExpectedVersion { get; set; } = "";

    /// <summary>
    /// Whether a version mismatch fails the build. False on a developer machine.
    /// </summary>
    /// <remarks>
    /// The pin is worth having on the build that produces artefacts other people consume, and is
    /// only an obstacle on the one that does not. Requiring an exact CLI locally means every
    /// developer, and every machine, has to chase a version to build at all - and the answer to
    /// "I pulled and it does not build" cannot be "install this specific tool first".
    /// <para>
    /// So the mismatch is a warning locally and an error in CI. The reproducibility argument is
    /// unchanged: what CI publishes is still produced by exactly one CLI version, and a local AST
    /// that came out different is a warning the developer has already seen.
    /// </para>
    /// </remarks>
    public bool PinVersion { get; set; }

    [Output]
    public ITaskItem? AstFile { get; set; }

    public override bool Execute() {
        var tool = ResolveTool();

        if (tool == null) {
            Log.LogError(null, "HSMT010", null, FirstModel(), 0, 0, 0, 0,
                "The Smithy CLI was not found. Install it and put it on PATH, or set " +
                "$(HardenedSmithyCliPath) to its location. A project can also skip the CLI " +
                "entirely by committing the AST and pointing @(HardenedSmithyAst) at it.");

            return false;
        }

        if (!CheckVersion(tool)) {
            return false;
        }

        return Generate(tool);
    }

    /// <summary>
    /// The CLI, from the explicit path or from PATH.
    /// </summary>
    /// <remarks>
    /// PATH is searched by hand rather than left to the process launcher, because with
    /// <c>UseShellExecute=false</c> Windows does not apply PATHEXT - and the CLI ships there as
    /// <c>smithy.bat</c>, so handing the launcher a bare "smithy" finds nothing on the one platform
    /// where the extension matters.
    /// </remarks>
    private string? ResolveTool() {
        if (!string.IsNullOrWhiteSpace(ToolPath)) {
            return File.Exists(ToolPath) ? ToolPath : null;
        }

        var names = Path.DirectorySeparatorChar == '\\'
            ? new[] { "smithy.bat", "smithy.cmd", "smithy.exe", "smithy" }
            : new[] { "smithy" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var directory in path.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(directory)) {
                continue;
            }

            foreach (var name in names) {
                string candidate;

                try {
                    candidate = Path.Combine(directory.Trim(), name);
                } catch (ArgumentException) {
                    // A malformed PATH entry is not worth failing the build over.
                    continue;
                }

                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        return null;
    }

    private bool CheckVersion(string tool) {
        if (string.IsNullOrWhiteSpace(ExpectedVersion)) {
            return true;
        }

        var result = Run(tool, new[] { "--version" }, null);

        if (result.ExitCode != 0) {
            Log.LogError(null, "HSMT011", null, FirstModel(), 0, 0, 0, 0,
                "'{0} --version' failed with exit code {1}. {2}",
                tool, result.ExitCode, result.StandardError.Trim());

            return false;
        }

        var actual = result.StandardOutput.Trim();

        if (!string.Equals(actual, ExpectedVersion.Trim(), StringComparison.Ordinal)) {
            const string message =
                "The Smithy CLI at '{0}' is version {1}, but this build is pinned to {2}. The AST a " +
                "different version produces can differ, so the generated code would differ with it.";

            if (PinVersion) {
                Log.LogError(null, "HSMT011", null, FirstModel(), 0, 0, 0, 0,
                    message + " Install {2}, or change $(HardenedSmithyCliVersion) deliberately.",
                    tool, actual, ExpectedVersion.Trim());

                return false;
            }

            Log.LogWarning(null, "HSMT011", null, FirstModel(), 0, 0, 0, 0,
                message + " Building anyway, because the pin is enforced on the build that publishes " +
                "rather than on yours. Set $(HardenedSmithyPinCliVersion) to make this an error here too.",
                tool, actual, ExpectedVersion.Trim());
        }

        Log.LogMessage(MessageImportance.Low, "Smithy CLI {0} at {1}.", actual, tool);

        return true;
    }

    private bool Generate(string tool) {
        var arguments = new List<string> { "ast", "--flatten" };

        foreach (var model in Models) {
            arguments.Add(model.GetMetadata("FullPath"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath))!);

        // Written beside the target and moved into place, so a failed run cannot leave a truncated
        // or empty file where the next up-to-date check would accept it as a model.
        var staging = OutputPath + ".tmp";
        var result = Run(tool, arguments, staging);

        if (result.ExitCode != 0) {
            Delete(staging);

            // The CLI's diagnostics name the .smithy file and line already, so they are passed
            // through rather than summarised - and reported against the model rather than against
            // this target, because that is the file the author edits.
            Log.LogError(null, "HSMT012", null, FirstModel(), 0, 0, 0, 0,
                "The Smithy CLI could not read the model (exit code {0}).\n{1}",
                result.ExitCode, result.StandardError.TrimEnd());

            return false;
        }

        // Success with nothing written is the shape of a CLI that validated and declined to emit.
        // Treated as a failure rather than parsed, because an empty AST reaches the reader as an
        // unhelpful error about position zero.
        if (!File.Exists(staging) || new FileInfo(staging).Length == 0) {
            Delete(staging);

            Log.LogError(null, "HSMT012", null, FirstModel(), 0, 0, 0, 0,
                "The Smithy CLI exited successfully but wrote no AST.{0}",
                result.StandardError.Length > 0 ? "\n" + result.StandardError.TrimEnd() : "");

            return false;
        }

        // Anything the CLI said on a successful run is a warning it got away with, not an error.
        if (result.StandardError.Trim().Length > 0) {
            Log.LogWarning(null, "HSMT013", null, FirstModel(), 0, 0, 0, 0,
                "{0}", result.StandardError.Trim());
        }

        MoveIfChanged(staging, OutputPath);

        AstFile = new Microsoft.Build.Utilities.TaskItem(OutputPath);

        return true;
    }

    /// <summary>
    /// Replaces the AST only when its content actually changed.
    /// </summary>
    /// <remarks>
    /// Rewriting an identical file would bump its timestamp, and everything downstream keys off
    /// that - the extract task's Inputs/Outputs check and the compiler's own. Editing a comment in a
    /// <c>.smithy</c> file would otherwise regenerate every model, every record and the whole
    /// compilation.
    /// </remarks>
    private static void MoveIfChanged(string staging, string destination) {
        if (File.Exists(destination) &&
            File.ReadAllText(destination) == File.ReadAllText(staging)) {
            Delete(staging);

            return;
        }

        if (File.Exists(destination)) {
            File.Delete(destination);
        }

        File.Move(staging, destination);
    }

    private static void Delete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (IOException) {
            // A leftover temp file is not worth failing a build over.
        }
    }

    private string FirstModel() =>
        Models.Length > 0 ? Models[0].GetMetadata("FullPath") : OutputPath;

    /// <summary>
    /// Runs the tool, sending standard output to a file when one is named.
    /// </summary>
    private static ProcessResult Run(string tool, IReadOnlyList<string> arguments, string? stdoutPath) {
        var startInfo = new ProcessStartInfo {
            FileName = tool,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

#if NETFRAMEWORK
        // ArgumentList is not on .NET Framework, so the arguments are quoted by hand for the one
        // host that runs this flavour - Visual Studio's MSBuild, which is Windows. A model path with
        // a space in it is the ordinary case this protects.
        startInfo.Arguments = string.Join(" ", arguments.Select(Quote));
#else
        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }
#endif

        var error = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.ErrorDataReceived += (_, e) => {
            if (e.Data != null) {
                error.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        var output = new StringBuilder();

        if (stdoutPath == null) {
            output.Append(process.StandardOutput.ReadToEnd());
        } else {
            // Straight from the pipe to the file. The AST is megabytes for a large model and there
            // is no reason for any of it to exist as a string.
            using var file = new FileStream(stdoutPath, FileMode.Create, FileAccess.Write);

            process.StandardOutput.BaseStream.CopyTo(file);
        }

        process.WaitForExit();

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

#if NETFRAMEWORK
    /// <summary>One argument, quoted the way the Windows command line parser reads it.</summary>
    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '"', '\t' }) < 0
            ? argument
            : "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
#endif

    private readonly struct ProcessResult {
        public ProcessResult(int exitCode, string standardOutput, string standardError) {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }
}
