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
internal sealed class TaskHarness : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hardened-openapi-task-tests",
        Guid.NewGuid().ToString("n")
    );

    public TaskHarness()
    {
        Directory.CreateDirectory(SpecDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public string SpecDirectory => Path.Combine(_root, "specs");

    public string OutputDirectory => Path.Combine(_root, "obj");

    public string GeneratedSourceDirectory => Path.Combine(OutputDirectory, "generated");

    /// <summary>Where every run keeps its warnings for <see cref="Replay"/>.</summary>
    public string WarningsFile => Path.Combine(OutputDirectory, "warnings.txt");

    public string WriteSpec(string fileName, string content)
    {
        var path = Path.Combine(SpecDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public Result Run(params string[] specPaths) => RunWithResponseModel("", specPaths);

    public Result RunWithResponseModel(string responseModel, params string[] specPaths)
    {
        var engine = new RecordingBuildEngine();

        var task = new ExtractOpenApiSpec
        {
            BuildEngine = engine,
            Specs = specPaths
                .Select(path => (ITaskItem)new Microsoft.Build.Utilities.TaskItem(path))
                .ToArray(),
            OutputDirectory = OutputDirectory,
            GeneratedSourceDirectory = GeneratedSourceDirectory,
            Namespace = "Test.Api",
            ResponseModel = responseModel,
            WarningsFile = WarningsFile,
        };

        var succeeded = task.Execute();

        return new Result(
            succeeded,
            engine.Errors,
            engine.Warnings,
            task.ModelFiles.Select(item => item.ItemSpec).ToArray(),
            task.GeneratedSources.Select(item => item.ItemSpec).ToArray()
        )
        {
            Ran = task.Ran,
        };
    }

    /// <summary>What a build that skipped the task reports, from the file the last run kept.</summary>
    public IReadOnlyList<BuildWarningEventArgs> Replay()
    {
        var engine = new RecordingBuildEngine();

        new Hardened.Idl.BuildTask.ReplaySpecWarnings
        {
            BuildEngine = engine,
            WarningsFile = WarningsFile,
        }.Execute();

        return engine.Warnings;
    }

    public string ModelPathFor(string specFileName) =>
        Path.Combine(
            OutputDirectory,
            Path.GetFileNameWithoutExtension(specFileName) + ".openapi-model.txt"
        );

    public string SourcePathFor(string specFileName) =>
        Path.Combine(
            GeneratedSourceDirectory,
            Path.GetFileNameWithoutExtension(specFileName) + ".g.cs"
        );

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    internal sealed record Result(
        bool Succeeded,
        IReadOnlyList<BuildErrorEventArgs> Errors,
        IReadOnlyList<BuildWarningEventArgs> Warnings,
        IReadOnlyList<string> ModelFiles,
        IReadOnlyList<string> GeneratedSources
    )
    {
        public bool Ran { get; init; }

        public bool HasError(string code) => Errors.Any(error => error.Code == code);

        public int WarningCount(string code) => Warnings.Count(warning => warning.Code == code);

        public string ErrorText =>
            string.Join("\n", Errors.Select(error => $"{error.Code}: {error.Message}"));
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public List<BuildWarningEventArgs> Warnings { get; } = new();

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs
        ) => true;

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";
    }
}
