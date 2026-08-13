using System.Collections;
using Microsoft.Build.Framework;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Runs <see cref="ExtractOpenApiSpec"/> against real files in a temporary directory and collects
/// what it logged.
/// </summary>
/// <remarks>
/// The task's whole reason for existing is that it may touch the file system, so it is exercised
/// against one rather than against an abstraction over one.
/// </remarks>
internal sealed class TaskHarness : IDisposable {

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hardened-openapi-task-tests", Guid.NewGuid().ToString("n"));

    public TaskHarness() {
        Directory.CreateDirectory(SpecDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public string SpecDirectory => Path.Combine(_root, "specs");

    public string OutputDirectory => Path.Combine(_root, "obj");

    public string GeneratedSourceDirectory => Path.Combine(OutputDirectory, "generated");

    public string StampFile => Path.Combine(OutputDirectory, "openapi.stamp");

    public string WriteSpec(string fileName, string content) {
        var path = Path.Combine(SpecDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public Result Run(params string[] specPaths) {
        var engine = new RecordingBuildEngine();

        var task = new ExtractOpenApiSpec {
            BuildEngine = engine,
            Specs = specPaths.Select(path => (ITaskItem)new Microsoft.Build.Utilities.TaskItem(path)).ToArray(),
            OutputDirectory = OutputDirectory,
            GeneratedSourceDirectory = GeneratedSourceDirectory,
            StampFile = StampFile,
            Namespace = "Test.Api",
        };

        var succeeded = task.Execute();

        return new Result(
            succeeded,
            engine.Errors,
            task.ModelFiles.Select(item => item.ItemSpec).ToArray(),
            task.GeneratedSources.Select(item => item.ItemSpec).ToArray());
    }

    public string ModelPathFor(string specFileName) =>
        Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(specFileName) + ".openapi-model.txt");

    public void Dispose() {
        try {
            Directory.Delete(_root, recursive: true);
        } catch (IOException) {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    internal sealed record Result(
        bool Succeeded,
        IReadOnlyList<BuildErrorEventArgs> Errors,
        IReadOnlyList<string> ModelFiles,
        IReadOnlyList<string> GeneratedSources) {
        public bool HasError(string code) => Errors.Any(error => error.Code == code);

        public string ErrorText => string.Join("\n", Errors.Select(error => $"{error.Code}: {error.Message}"));
    }

    private sealed class RecordingBuildEngine : IBuildEngine {
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) { }

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";
    }
}
