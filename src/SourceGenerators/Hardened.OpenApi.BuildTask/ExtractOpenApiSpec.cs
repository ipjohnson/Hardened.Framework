using Hardened.OpenApi.BuildTask.Filtering;
using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.Idl.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Hardened.Idl;

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
    /// Whether the source document is embedded so the application can serve it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off unless asked for, and per-spec <c>EmbedDocument</c> metadata overrides it either way.
    /// Serving your own description is a decision about public surface, not a default worth
    /// inheriting: it publishes every path, schema, example and vendor extension the document
    /// carries, including whatever is in there that was never meant to leave the building.
    /// </para>
    /// <para>
    /// The cost is invisible and unbounded. The document goes in as one string literal, so GitHub's
    /// 9.4 MB lands in the assembly's user string heap and exceeds its limit on its own - CS8103,
    /// from a project whose own models are a few kilobytes, with nothing in the message to connect
    /// it to a setting nobody chose.
    /// </para>
    /// </remarks>
    public bool EmbedDocument { get; set; }

    /// <summary>
    /// Whether operations with no tag are grouped by first path segment.
    /// </summary>
    /// <remarks>
    /// Off, so an untagged document produces one service, which is what most of them mean. Turn it
    /// on for a description that carries no tags at all and hundreds of operations - DigitalOcean's
    /// is one - where a single interface with every method on it is not what anyone implements.
    /// </remarks>
    public bool GroupUntaggedByPath { get; set; }

    /// <summary>
    /// Whether <c>$ref</c>s into other files are followed.
    /// </summary>
    /// <remarks>
    /// Off, and deliberately. A reference may name any URL, so following them turns a build into
    /// something that reaches the network - not reproducible, and not safe against a description
    /// someone else publishes. On, resolution is rooted at the directory holding the specification.
    /// </remarks>
    public bool LoadExternalRefs { get; set; }

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

    /// <summary>
    /// Turns one document into the neutral model everything downstream works on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only line in this task that knows what an OpenAPI document is. Everything past it — the
    /// emitters, the validation, the diagnostics, the source generator that reads the written model
    /// — operates on <see cref="ServiceSpecModel"/> and contains no OpenAPI concept.
    /// </para>
    /// <para>
    /// <b>Private, not <c>protected virtual</c>.</b> A second IDL front end wants to override this,
    /// and the plan for one said to make it virtual now. It cannot be, yet, and finding out what
    /// that costs is worth more than the change would have been: this class is <c>sealed</c>, so a
    /// virtual member does not compile (CS0549), and <see cref="ServiceSpecModel"/> is internal, so
    /// a protected member returning it does not either (CS0050). The extension point is therefore
    /// an unseal plus a model promoted to public — real surface, on a shipped MSBuild task,
    /// widened for a subclass nobody has written.
    /// </para>
    /// <para>
    /// So the parsing is named and isolated here, which is the part that pays off either way, and
    /// the widening waits for the front end whose requirements would justify it. Making this
    /// <c>protected virtual</c> then is one keyword and one accessibility change. That is the same
    /// reasoning that keeps an <c>ISpecParser</c> interface out: a seam guessed before its second
    /// implementation is a guess frozen into the shape of the task.
    /// </para>
    /// </remarks>
    private ServiceSpecModel? Parse(
        string document, string fileName, string specPath, ICollection<string> diagnostics) =>
        OpenApiSpecParser.Parse(
            document, fileName, CancellationToken.None, ApplyServerBasePath, diagnostics,
            GroupUntaggedByPath,
            LoadExternalRefs ? Path.GetDirectoryName(Path.GetFullPath(specPath)) : null);

    /// <summary>
    /// Narrows a document to the operations one service implements. Returns true if that failed.
    /// </summary>
    /// <remarks>
    /// The filter is per-spec metadata rather than a task property, because one description is
    /// routinely sliced differently by several projects - which is the point of slicing it at all.
    /// </remarks>
    private bool Slice(ITaskItem spec, string path, ServiceSpecModel model, out bool failed) {
        failed = false;
        var filter = new SpecSlicer.Filter {
            IncludePaths = SplitMetadata(spec, "IncludePaths"),
            ExcludePaths = SplitMetadata(spec, "ExcludePaths"),
            Tags = SplitMetadata(spec, "Tags")
        };

        if (filter.IsEmpty) {
            return false;
        }

        var result = SpecSlicer.Apply(model, filter);

        if (result.MatchedNothing) {
            failed = true;

            Log.LogError(null, "HOAT007", null, path, 0, 0, 0, 0,
                "The slice of '{0}' selected no operations, so nothing would be generated. " +
                "IncludePaths='{1}' ExcludePaths='{2}' Tags='{3}'.",
                path, spec.GetMetadata("IncludePaths"), spec.GetMetadata("ExcludePaths"),
                spec.GetMetadata("Tags"));

            return true;
        }

        // Should be unreachable: the closure keeps everything a surviving operation reaches. A hole
        // in it would otherwise degrade to JsonElement without saying so.
        foreach (var dangling in result.DanglingReferences) {
            Log.LogWarning(null, "HOAT008", null, path, 0, 0, 0, 0,
                "The slice of '{0}' removed a schema that is still referenced: {1}. The reference " +
                "degrades to JsonElement.", path, dangling);
        }

        Log.LogMessage(MessageImportance.Normal,
            "Sliced '{0}' to {1} operations ({2} dropped) and {3} schemas ({4} dropped).",
            path, result.OperationsKept, result.OperationsDropped,
            result.SchemasKept, result.SchemasDropped);

        return true;
    }

    /// <summary>
    /// The document the application will serve, or nothing.
    /// </summary>
    /// <remarks>
    /// Embedding the source text verbatim is what makes a served contract exact - see
    /// <c>SpecificationDocumentEmitter</c>. That argument holds while the application implements
    /// the whole document; a slice implements part of it, so the original would advertise
    /// operations that answer 404 and schemas no handler can produce. Asked for anyway, it is
    /// emitted and said out loud.
    /// </remarks>
    private string ServedDocument(ITaskItem spec, string path, string document, bool sliced) {
        var declared = spec.GetMetadata("EmbedDocument");

        var embed = string.IsNullOrWhiteSpace(declared)
            ? EmbedDocument
            : string.Equals(declared, "true", System.StringComparison.OrdinalIgnoreCase);

        if (!embed) {
            return "";
        }

        if (sliced) {
            Log.LogWarning(null, "HOAT009", null, path, 0, 0, 0, 0,
                "'{0}' is sliced but its document is embedded whole, so the application will serve " +
                "a description of operations it does not implement.", path);
        }

        return document;
    }

    /// <summary>Semicolon-separated metadata, as MSBuild lists are written.</summary>
    private static IReadOnlyList<string> SplitMetadata(ITaskItem spec, string name) {
        var value = spec.GetMetadata(name);

        if (string.IsNullOrWhiteSpace(value)) {
            return System.Array.Empty<string>();
        }

        var parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var trimmed = new List<string>(parts.Length);

        foreach (var part in parts) {
            var candidate = part.Trim();

            if (candidate.Length > 0) {
                trimmed.Add(candidate);
            }
        }

        return trimmed;
    }

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

            // The slice is part of the identity, matching what the targets file declared as
            // this spec's outputs. Two slices of one document otherwise write over each
            // other, and their generated helper types would collide on one name.
            var slice = spec.GetMetadata("Slice");

            var fileName = Path.GetFileNameWithoutExtension(path) +
                           (string.IsNullOrWhiteSpace(slice) ? "" : "." + slice.Trim());
            var document = File.ReadAllText(path);
            ServiceSpecModel model;

            // Whatever the reader had to say. Populated even on success, where it describes
            // something the document got away with rather than something that stopped it.
            var readerDiagnostics = new List<string>();

            try {
                var parsed = Parse(document, fileName, path, readerDiagnostics);

                if (parsed is null) {
                    Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                        "OpenAPI spec '{0}' could not be parsed{1}", path,
                        readerDiagnostics.Count > 0
                            ? ": " + string.Join("; ", readerDiagnostics.ToArray())
                            : ", and the reader gave no reason.");
                    continue;
                }

                // Not fatal - the document produced a model. Reported so a partially understood
                // spec does not look like a fully understood one.
                foreach (var diagnostic in readerDiagnostics) {
                    Log.LogWarning(null, "HOAT006", null, path, 0, 0, 0, 0,
                        "OpenAPI spec '{0}': {1}", path, diagnostic);
                }

                model = parsed;
            } catch (Exception exception) {
                // The file and line belong to the spec, not to this task: the author edits the
                // yaml, and a build error that points at an MSBuild target instead is noise.
                Log.LogError(null, "HOAT002", null, path, 0, 0, 0, 0,
                    "OpenAPI spec '{0}' could not be parsed: {1}", path, exception.Message);
                continue;
            }

            // Narrowed before anything is inspected, so the diagnostics below describe the code
            // that will actually be emitted rather than the whole document.
            var sliced = Slice(spec, path, model, out var sliceFailed);

            if (sliceFailed) {
                continue;
            }

            // Checked before anything is written. These describe C# that will not compile, and
            // emitting it anyway turns a fixable spec problem into a compiler error in a generated
            // file the author cannot edit.
            var problems = SpecDiagnostics.Find(model);
            var fatal = false;

            foreach (var problem in problems) {
                if (problem.Fatal) {
                    Log.LogError(null, problem.Code, null, path, 0, 0, 0, 0, "{0}", problem.Message);
                    fatal = true;
                } else {
                    // Already resolved. Reported so the choice is visible rather than discovered
                    // later in a generated file nobody opened.
                    Log.LogWarning(null, problem.Code, null, path, 0, 0, 0, 0, "{0}", problem.Message);
                }
            }

            if (fatal) {
                continue;
            }

            // Named here rather than derived independently on both sides. The generator is told
            // what the resolver is called, the same way it is told everything else.
            model.JsonTypeInfoResolverName = JsonTypeInfoEmitter.ResolverNameFor(fileName);

            var sourcePath = Path.Combine(GeneratedSourceDirectory, fileName + SourceSuffix);
            WriteIfChanged(sourcePath, Emit(model, ServedDocument(spec, path, document, sliced), path));
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
    private string Emit(ServiceSpecModel model, string document, string specPath) =>
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
