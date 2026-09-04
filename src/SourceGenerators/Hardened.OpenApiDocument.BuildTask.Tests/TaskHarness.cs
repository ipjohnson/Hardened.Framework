using System.Collections;
using Microsoft.Build.Framework;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// Runs <see cref="WriteOpenApiDocument"/> against a real assembly and a temporary output
/// directory, and collects what it logged.
/// </summary>
internal sealed class TaskHarness : IDisposable {

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hardened-openapi-document-tests", Guid.NewGuid().ToString("n"));

    public TaskHarness() {
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>A path under the harness directory, which the task creates on the way to writing.</summary>
    public string Under(string relative) => Path.Combine(_root, relative);

    /// <summary>The built assembly of one of the integration applications, beside this test's own.</summary>
    public static string Fixture(string assemblyName) =>
        Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

    public const string WebApp = "Hardened.IntegrationTests.WebApp.SUT";

    public const string OpenApiApp = "Hardened.IntegrationTests.OpenApi.SUT";

    public const string SmithyApp = "Hardened.IntegrationTests.Smithy.SUT";

    public Result Run(string assembly, string output, string version = "", string prefix = "HRDOA") {
        var engine = new RecordingBuildEngine();

        var task = new WriteOpenApiDocument {
            BuildEngine = engine,
            Assembly = assembly,
            Output = output,
            ProjectDirectory = _root,
            Version = version,
            DiagnosticPrefix = prefix
        };

        var succeeded = task.Execute();

        return new Result(succeeded, engine.Errors, engine.Warnings, task.WrittenPath, task.Changed);
    }

    public void Dispose() {
        try {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    internal sealed record Result(
        bool Succeeded,
        IReadOnlyList<BuildErrorEventArgs> Errors,
        IReadOnlyList<BuildWarningEventArgs> Warnings,
        string WrittenPath,
        bool Changed) {

        public bool HasError(string code) => Errors.Any(error => error.Code == code);

        public string ErrorText => string.Join("\n", Errors.Select(error => $"{error.Code}: {error.Message}"));

        public string WarningText => string.Join("\n", Warnings.Select(warning => $"{warning.Code}: {warning.Message}"));
    }

    private sealed class RecordingBuildEngine : IBuildEngine {
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public List<BuildWarningEventArgs> Warnings { get; } = new();

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";
    }
}
