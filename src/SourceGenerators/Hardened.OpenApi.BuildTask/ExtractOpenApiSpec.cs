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

    /// <summary>
    /// File written last, once everything else has succeeded, and the target's only declared output.
    /// </summary>
    /// <remarks>
    /// The set of emitted sources depends on the contents of each spec, so it cannot be predicted
    /// from the item list the way the model paths can - and a target whose Inputs/Outputs check
    /// covers only some of what it produces reports itself up to date while the rest is missing.
    /// One stamp covers all of it. Its contents are the files that were written, so a diff explains
    /// what changed.
    /// </remarks>
    [Required]
    public string StampFile { get; set; } = "";

    /// <summary>Root namespace for the emitted types.</summary>
    public string Namespace { get; set; } = "Generated";

    /// <summary>Whether emitted types carry <c>[ExcludeFromCodeCoverage]</c>.</summary>
    public bool ExcludeFromCoverage { get; set; } = true;

    /// <summary>The written models, for the caller to add to <c>@(AdditionalFiles)</c>.</summary>
    [Output]
    public ITaskItem[] ModelFiles { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>The emitted C#, for the caller to add to <c>@(Compile)</c>.</summary>
    [Output]
    public ITaskItem[] GeneratedSources { get; set; } = System.Array.Empty<ITaskItem>();

    public override bool Execute() {
        var models = new List<ITaskItem>();
        var sources = new List<ITaskItem>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            OpenApiSpecModel model;

            try {
                var parsed = OpenApiSpecParser.Parse(File.ReadAllText(path), fileName, CancellationToken.None);

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

            // Named here rather than derived independently on both sides. The generator is told
            // what the resolver is called, the same way it is told everything else.
            model.JsonTypeInfoResolverName = JsonTypeInfoEmitter.ResolverNameFor(fileName);

            foreach (var source in Emit(model)) {
                var sourcePath = Path.Combine(GeneratedSourceDirectory, source.Key);
                WriteIfChanged(sourcePath, source.Value);
                written.Add(sourcePath);
                sources.Add(new TaskItem(sourcePath));
            }

            var modelPath = Path.Combine(OutputDirectory, fileName + ModelSuffix);
            WriteIfChanged(modelPath, SpecModelSerializer.Write(model));

            var item = new TaskItem(modelPath);
            item.SetMetadata("SpecPath", path);
            models.Add(item);
        }

        ModelFiles = models.ToArray();
        GeneratedSources = sources.ToArray();

        if (Log.HasLoggedErrors) {
            // The stamp is deliberately not written, so the next build re-runs rather than
            // reporting itself up to date against a half-emitted directory.
            return false;
        }

        RemoveStaleSources(written);

        var stampDirectory = Path.GetDirectoryName(StampFile);

        if (!string.IsNullOrEmpty(stampDirectory)) {
            Directory.CreateDirectory(stampDirectory);
        }

        File.WriteAllText(StampFile, string.Join("\n", models.Concat(sources).Select(item => item.ItemSpec).OrderBy(path => path, StringComparer.Ordinal)));

        return true;
    }

    /// <summary>
    /// Everything that follows from the spec alone: one file per schema, one per service interface,
    /// one resolver, one per generated filter attribute.
    /// </summary>
    private IEnumerable<KeyValuePair<string, string>> Emit(OpenApiSpecModel model) {
        foreach (var schema in model.Schemas) {
            var source = schema.Kind switch {
                SchemaKind.Object => RecordEmitter.Emit(schema, Namespace, ExcludeFromCoverage),
                SchemaKind.Enum => EnumEmitter.Emit(schema, Namespace),
                _ => null,
            };

            if (source is not null) {
                yield return new KeyValuePair<string, string>($"{model.FileName}.{schema.Name}{SourceSuffix}", source);
            }
        }

        foreach (var service in model.Services) {
            yield return new KeyValuePair<string, string>(
                $"{model.FileName}.{NamingHelper.ToInterfaceName(service.Tag)}{SourceSuffix}",
                ServiceInterfaceEmitter.Emit(service, Namespace));
        }

        yield return new KeyValuePair<string, string>(
            $"{model.FileName}.{model.JsonTypeInfoResolverName}{SourceSuffix}",
            JsonTypeInfoEmitter.Emit(model.Schemas, Namespace, model.FileName, ExcludeFromCoverage));

        foreach (var filterType in model.FilterTypes) {
            if (!filterType.Generate) {
                continue;
            }

            yield return new KeyValuePair<string, string>(
                $"{model.FileName}.{filterType.ClassName}{SourceSuffix}",
                FilterTypeEmitter.Emit(filterType, ExcludeFromCoverage));
        }
    }

    /// <summary>
    /// Deletes generated files this run did not produce.
    /// </summary>
    /// <remarks>
    /// A Roslyn generator's output vanishes when it stops emitting it. Files on disk do not: rename
    /// a schema and the old record keeps compiling, so the build stays green against a type the spec
    /// no longer declares. Only ever removes the suffix this task writes, and only when the run
    /// succeeded - clearing the directory after a parse failure would turn one bad spec into a
    /// project-wide cascade of missing types.
    /// </remarks>
    private void RemoveStaleSources(HashSet<string> written) {
        foreach (var existing in Directory.GetFiles(GeneratedSourceDirectory, "*" + SourceSuffix)) {
            if (written.Contains(existing)) {
                continue;
            }

            try {
                File.Delete(existing);
            } catch (IOException) {
                // Losing the race with an editor holding the file open is not worth failing over;
                // the compiler will report the duplicate if it actually matters.
            }
        }
    }

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
