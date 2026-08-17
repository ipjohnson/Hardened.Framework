using System.Collections;
using Microsoft.Build.Framework;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// Runs <see cref="ExtractSmithySpec"/> against real files in a temporary directory and collects
/// what it logged.
/// </summary>
/// <remarks>
/// The same shape as the OpenAPI task's harness, and for the same reason: the task exists to touch
/// the file system, so it is exercised against one rather than against an abstraction over one.
/// </remarks>
internal sealed class SmithyTaskHarness : IDisposable {

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hardened-smithy-task-tests", Guid.NewGuid().ToString("n"));

    public SmithyTaskHarness() {
        Directory.CreateDirectory(AstDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public string AstDirectory => Path.Combine(_root, "ast");

    public string OutputDirectory => Path.Combine(_root, "obj");

    public string GeneratedSourceDirectory => Path.Combine(OutputDirectory, "generated");

    public string WriteAst(string fileName, string content) {
        var path = Path.Combine(AstDirectory, fileName);

        File.WriteAllText(path, content);

        return path;
    }

    public Result Run(string serviceShapeId, params string[] astPaths) {
        var engine = new RecordingBuildEngine();

        var task = new ExtractSmithySpec {
            BuildEngine = engine,
            Specs = astPaths
                .Select(path => (ITaskItem)new Microsoft.Build.Utilities.TaskItem(path))
                .ToArray(),
            OutputDirectory = OutputDirectory,
            GeneratedSourceDirectory = GeneratedSourceDirectory,
            Namespace = "Test.Api",
            ServiceShapeId = serviceShapeId
        };

        var succeeded = task.Execute();

        return new Result(
            succeeded,
            engine.Errors,
            engine.Warnings,
            task.ModelFiles.Select(item => item.ItemSpec).ToArray(),
            task.GeneratedSources.Select(item => item.ItemSpec).ToArray());
    }

    /// <summary>
    /// The model suffix is shared with the OpenAPI front end on purpose - the file holds a
    /// ServiceSpecModel, and sharing it is what lets the existing source generator read this one.
    /// </summary>
    public string ModelPathFor(string astFileName) =>
        Path.Combine(OutputDirectory,
            Path.GetFileNameWithoutExtension(astFileName) + ".openapi-model.txt");

    public string SourcePathFor(string astFileName) =>
        Path.Combine(GeneratedSourceDirectory,
            Path.GetFileNameWithoutExtension(astFileName) + ".g.cs");

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
        IReadOnlyList<BuildWarningEventArgs> Warnings,
        IReadOnlyList<string> ModelFiles,
        IReadOnlyList<string> GeneratedSources) {

        public bool HasError(string code) => Errors.Any(error => error.Code == code);

        public bool HasWarning(string code) => Warnings.Any(warning => warning.Code == code);

        public string ErrorText =>
            string.Join("\n", Errors.Select(error => $"{error.Code}: {error.Message}"));

        public string WarningText =>
            string.Join("\n", Warnings.Select(warning => $"{warning.Code}: {warning.Message}"));
    }

    internal sealed class RecordingBuildEngine : IBuildEngine {
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public List<BuildWarningEventArgs> Warnings { get; } = new();

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName, string[] targetNames,
            IDictionary globalProperties, IDictionary targetOutputs) => true;

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";
    }
}
