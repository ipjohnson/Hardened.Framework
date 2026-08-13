using Hardened.OpenApi.SourceGenerator;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Hardened.OpenApi.BuildTask;

/// <summary>
/// Reads each OpenAPI document and writes a normalised model beside it in <c>obj/</c>, for the
/// source generator to pick up as an <c>AdditionalFile</c>.
/// </summary>
/// <remarks>
/// <para>
/// The spec is parsed once, here, and never opened again. That is what lets
/// <c>Hardened.OpenApi.SourceGenerator</c> ship without Microsoft.OpenApi, Microsoft.OpenApi.Readers
/// and SharpYaml embedded as resources, without the <c>AssemblyResolve</c> hook that loaded them,
/// and without the RS1035 suppression that hook required.
/// </para>
/// <para>
/// Running in MSBuild rather than in Roslyn also means file access is ordinary rather than banned,
/// and that errors carry a file and a line instead of arriving as a generator warning.
/// </para>
/// </remarks>
public sealed class ExtractOpenApiSpec : Microsoft.Build.Utilities.Task {

    /// <summary>The OpenAPI documents to read, from <c>@(HardenedOpenApiSpec)</c>.</summary>
    [Required]
    public ITaskItem[] Specs { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>Directory the normalised models are written to, normally <c>$(IntermediateOutputPath)</c>.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>The written models, for the caller to add to <c>@(AdditionalFiles)</c>.</summary>
    [Output]
    public ITaskItem[] ModelFiles { get; set; } = System.Array.Empty<ITaskItem>();

    public override bool Execute() {
        var written = new List<ITaskItem>();

        Directory.CreateDirectory(OutputDirectory);

        foreach (var spec in Specs) {
            var path = spec.GetMetadata("FullPath");

            if (!File.Exists(path)) {
                Log.LogError(null, "HOAT001", null, path, 0, 0, 0, 0,
                    "OpenAPI spec '{0}' does not exist.", path);
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            var modelPath = Path.Combine(OutputDirectory, fileName + ".openapi-model.txt");

            try {
                var model = OpenApiSpecParser.Parse(File.ReadAllText(path), fileName, CancellationToken.None);

                if (model is null) {
                    Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                        "OpenAPI spec '{0}' could not be parsed.", path);
                    continue;
                }

                WriteIfChanged(modelPath, SpecModelSerializer.Write(model));
            } catch (Exception exception) {
                // The file and line belong to the spec, not to this task: the author edits the
                // yaml, and a build error that points at an MSBuild target instead is noise.
                Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                    "OpenAPI spec '{0}' could not be parsed: {1}", path, exception.Message);
                continue;
            }

            var item = new TaskItem(modelPath);
            item.SetMetadata("SpecPath", path);
            written.Add(item);
        }

        ModelFiles = written.ToArray();

        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Rewriting an unchanged model would bump its timestamp, and the generator treats every
    /// AdditionalFile change as a reason to re-run - so an untouched spec would still invalidate the
    /// whole generator on each build.
    /// </summary>
    private static void WriteIfChanged(string path, string content) {
        if (File.Exists(path) && File.ReadAllText(path) == content) {
            return;
        }

        File.WriteAllText(path, content);
    }
}
