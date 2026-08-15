using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Hardened.OpenApi.BuildTask;

/// <summary>
/// Reads each OpenAPI document, emits everything that is a pure spec-to-C# transformation, and
/// writes a normalised model for the source generator to pick up as an <c>AdditionalFile</c>.
/// </summary>
/// <remarks>
/// <para>
/// The spec is parsed once, here, and never opened again. That is what lets
/// <c>Hardened.OpenApi.SourceGenerator</c> ship without Microsoft.OpenApi, Microsoft.OpenApi.Readers
/// and SharpYaml embedded as resources, without the <c>AssemblyResolve</c> hook that loaded them,
/// and without the RS1035 suppression that hook required.
/// </para>
/// <para>
/// Records, enums, service interfaces, the JSON type info resolver and the filter attributes are all
/// written from here: none of them needs to look at the compilation, so none of them needs a Roslyn
/// generator. What stays in the generator is handler classes, which are matched against
/// <c>[Handler]</c> declarations, and the routing table, which is anchored on the entry point.
/// </para>
/// <para>
/// MSBuild runs before the compiler, so a file this puts into <c>@(Compile)</c> is indistinguishable
/// from one a human wrote. That is also what will let spec-driven patterns reach
/// <c>[GeneratedRegex]</c>, which a source generator cannot emit for itself.
/// </para>
/// </remarks>
public sealed class ExtractOpenApiSpec : Microsoft.Build.Utilities.Task {

    private const string ModelSuffix = ".openapi-model.txt";

    private const string SourceSuffix = ".g.cs";

    /// <summary>The OpenAPI documents to read, from <c>@(HardenedOpenApiSpec)</c>.</summary>
    [Required]
    public ITaskItem[] Specs { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>Directory the normalised models are written to.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>Directory the generated C# is written to.</summary>
    [Required]
    public string GeneratedSourceDirectory { get; set; } = "";

    /// <summary>Root namespace for the emitted types.</summary>
    public string Namespace { get; set; } = "Generated";

    /// <summary>Whether emitted types carry <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public bool ExcludeFromCoverage { get; set; } = true;

    /// <summary>
    /// Whether the path of the first <c>servers</c> entry prefixes every route.
    /// </summary>
    /// <remarks>
    /// Off unless asked for. See <c>OpenApiSpecParser.ServerBasePath</c> for why applying it
    /// unasked is the wrong default.
    /// </remarks>
    public bool ApplyServerBasePath { get; set; }

    /// <summary>The written models, for the caller to add to <c>@(AdditionalFiles)</c>.</summary>
    [Output]
    public ITaskItem[] ModelFiles { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>The emitted C#, for the caller to add to <c>@(Compile)</c>.</summary>
    [Output]
    public ITaskItem[] GeneratedSources { get; set; } = System.Array.Empty<ITaskItem>();

    public override bool Execute() {
        var models = new List<ITaskItem>();
        var sources = new List<ITaskItem>();

        Directory.CreateDirectory(OutputDirectory);
        Directory.CreateDirectory(GeneratedSourceDirectory);

        foreach (var spec in Specs) {
            var path = spec.GetMetadata("FullPath");

            if (!File.Exists(path)) {
                Log.LogError(null, "HOAT001", null, path, 0, 0, 0, 0,
                    "OpenAPI spec '{0}' does not exist.", path);
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            var document = File.ReadAllText(path);
            OpenApiSpecModel model;

            try {
                var parsed = OpenApiSpecParser.Parse(
                    document, fileName, CancellationToken.None, ApplyServerBasePath);

                if (parsed is null) {
                    Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                        "OpenAPI spec '{0}' could not be parsed.", path);
                    continue;
                }

                model = parsed;
            } catch (Exception exception) {
                // The file and line belong to the spec, not to this task: the author edits the
                // yaml, and a build error that points at an MSBuild target instead is noise.
                Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                    "OpenAPI spec '{0}' could not be parsed: {1}", path, exception.Message);
                continue;
            }

            // Checked before anything is written. These describe C# that will not compile, and
            // emitting it anyway turns a fixable spec problem into a compiler error in a generated
            // file the author cannot edit.
            var problems = SpecDiagnostics.Find(model);

            if (problems.Count > 0) {
                foreach (var problem in problems) {
                    Log.LogError(null, problem.Code, null, path, 0, 0, 0, 0, "{0}", problem.Message);
                }

                continue;
            }

            // Named here rather than derived independently on both sides. The generator is told
            // what the resolver is called, the same way it is told everything else.
            model.JsonTypeInfoResolverName = JsonTypeInfoEmitter.ResolverNameFor(fileName);

            var sourcePath = Path.Combine(GeneratedSourceDirectory, fileName + SourceSuffix);
            WriteIfChanged(sourcePath, Emit(model, document, path));
            sources.Add(new TaskItem(sourcePath));

            var modelPath = Path.Combine(OutputDirectory, fileName + ModelSuffix);
            WriteIfChanged(modelPath, SpecModelSerializer.Write(model));

            var item = new TaskItem(modelPath);
            item.SetMetadata("SpecPath", path);
            models.Add(item);
        }

        ModelFiles = models.ToArray();
        GeneratedSources = sources.ToArray();

        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Everything that follows from the spec alone, as one file.
    /// </summary>
    /// <remarks>
    /// One file per spec rather than one per type, which is what a Roslyn generator would have to do
    /// - it addresses its output by hint name and every name must be unique. On disk there is no
    /// such rule, and a single file is what keeps this target's incrementality honest: its outputs
    /// are then derivable from the item list without reading a spec, which one file per type is not.
    /// </remarks>
    private string Emit(OpenApiSpecModel model, string document, string specPath) =>
        SpecFileEmitter.Emit(model, Namespace, ExcludeFromCoverage, document, specPath);

    /// <summary>
    /// Rewriting an unchanged file would bump its timestamp, and both the compiler's up-to-date
    /// check and the generator's incremental cache key off that - so an untouched spec would
    /// invalidate the whole compilation on every build.
    /// </summary>
    private static void WriteIfChanged(string path, string content) {
        if (File.Exists(path) && File.ReadAllText(path) == content) {
            return;
        }

        File.WriteAllText(path, content);
    }
}
