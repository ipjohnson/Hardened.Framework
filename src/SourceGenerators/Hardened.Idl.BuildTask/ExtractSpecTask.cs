using Hardened.Idl;
using Hardened.Idl.Emitters;
using Hardened.Idl.Filtering;
using Hardened.Idl.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Hardened.Idl.BuildTask;

/// <summary>
/// Everything a description-to-C# build task does that is not reading the description.
/// </summary>
/// <remarks>
/// <para>
/// Slicing, diagnostics, naming, emitting and writing are identical whatever language the
/// description was written in, because all of them operate on <see cref="ServiceSpecModel"/> and
/// none of them opens a document. A front end supplies <see cref="Parse"/> and three strings that
/// name itself in diagnostics; it inherits the rest.
/// </para>
/// <para>
/// <b>Linked as source, not referenced as an assembly.</b> <see cref="ServiceSpecModel"/> is
/// internal, so each front end compiles its own copy of the IR into its own assembly. A shared base
/// class in a third assembly would therefore name a different type than the front end does, and
/// would not bind. Linking the source puts the shell and the IR in the same assembly, which is what
/// makes <see cref="Parse"/> expressible at all.
/// </para>
/// <para>
/// <b>Why <see cref="Parse"/> is <c>internal abstract</c> rather than <c>protected abstract</c>.</b>
/// A protected member returning an internal type does not compile (CS0050), which is what previously
/// made this extension point look like it cost an unsealed task and a model promoted to public. It
/// does not: an internal abstract member on a public class is legal, and it says exactly the right
/// thing - the class can be derived from inside this assembly and nowhere else, which is the only
/// place a front end can live given the IR is compiled in alongside it. No consumer surface widens.
/// </para>
/// </remarks>
public abstract class ExtractSpecTask : Microsoft.Build.Utilities.Task {

    /// <summary>
    /// The suffix every front end writes its normalised model with.
    /// </summary>
    /// <remarks>
    /// Deliberately shared, and deliberately still spelled for OpenAPI. The file holds a
    /// <see cref="ServiceSpecModel"/> rather than any particular description, and the source
    /// generator matches this one suffix at <c>OpenApiSourceGenerator.IsSpecModelFile</c> - so
    /// sharing it is what lets a second front end reuse the entire generator half for no code. The
    /// name lies; changing it in three agreeing places to fix that is the more expensive problem.
    /// </remarks>
    protected const string ModelSuffix = ".openapi-model.txt";

    protected const string SourceSuffix = ".g.cs";

    /// <summary>The descriptions to read.</summary>
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
    /// How a generated service interface states an operation's declared responses - "Standard",
    /// "Response" or "Union".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build property rather than <c>[ResponseModel]</c> on the entry point, which is what the
    /// plan named. That attribute is not readable here: this task runs before the compiler - the
    /// same ordering that stops it from seeing routes - and an attribute is something a compiler
    /// reads. <c>HardenedOpenApiVersion</c> is already the precedent for a build property deciding
    /// what the emitter writes.
    /// </para>
    /// <para>
    /// An unrecognised value is Standard rather than a build failure. The property is set by hand
    /// in a csproj with no completion behind it, and failing every build of a project that
    /// mistyped it would be a worse trade than generating the interfaces it had yesterday.
    /// </para>
    /// </remarks>
    public string ResponseModel { get; set; } = "";

    /// <summary>
    /// Whether the source document is embedded so the application can serve it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, and per-spec <c>EmbedDocument</c> metadata overrides it either way. It was off, on the
    /// grounds that it publishes the whole description and a large one exceeded the metadata limit
    /// on user strings by itself - which was true while the document was embedded as a UTF-16 string
    /// literal. It is gzipped into a byte blob now, about an eighth the size and on a different heap,
    /// so the cost that made off the right default is gone.
    /// </para>
    /// <para>
    /// Embedding is not publishing. Nothing is served until a spec names a <c>PublishUrl</c>; what
    /// this decides is only whether the document is there to serve.
    /// </para>
    /// </remarks>
    public bool EmbedDocument { get; set; } = true;

    /// <summary>
    /// Whether schemas nothing in the description reaches are generated anyway.
    /// </summary>
    /// <remarks>
    /// Off. A description declares shapes; it does not promise any operation uses them. This also
    /// does the work of dropping whatever a front end's own dependencies dragged in - a Smithy model
    /// that declares maven dependencies carries their trait definitions as shapes, and none of them
    /// is reachable from an operation.
    /// </remarks>
    public bool EmitUnreferencedSchemas { get; set; }

    /// <summary>The written models, for the caller to add to <c>@(AdditionalFiles)</c>.</summary>
    [Output]
    public ITaskItem[] ModelFiles { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>The emitted C#, for the caller to add to <c>@(Compile)</c>.</summary>
    [Output]
    public ITaskItem[] GeneratedSources { get; set; } = System.Array.Empty<ITaskItem>();

    /// <summary>
    /// Turns one description into the neutral model everything downstream works on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only member that knows what language the description is written in.
    /// </para>
    /// <para>
    /// <b>An implementation must return a model whose names are already allocated</b> - it ends by
    /// calling <c>NameAllocator.Apply(model, fileName)</c>, as <c>OpenApiSpecParser</c> does. That
    /// looks like something this shell should do instead, and it cannot be, for two reasons.
    /// Naming has to run after whatever reshaping the language needs - references inlined,
    /// inheritance a record cannot carry dropped, undecidable choices removed - because each of
    /// those changes which types survive to be named, and all of them are the front end's. And the
    /// parse entry point is what forty-odd tests call directly, precisely so they exercise what the
    /// task exercises; a parse that returned unnamed models would make every one of them assert
    /// against something the build never sees.
    /// </para>
    /// <para>
    /// So this is a contract rather than a step: return a model that is normalised <em>and</em>
    /// named. A front end that skips the naming pass emits colliding types, and nothing fails until
    /// the generated file reaches the compiler.
    /// </para>
    /// </remarks>
    internal abstract ServiceSpecModel? Parse(
        string document, string fileName, string specPath, ICollection<string> diagnostics);

    /// <summary>The diagnostic code prefix this front end reports under - <c>HOAT</c> and friends.</summary>
    protected abstract string DiagnosticPrefix { get; }

    /// <summary>What this front end calls the thing it reads, for diagnostics.</summary>
    protected abstract string SpecNoun { get; }

    /// <summary>The MSBuild property that turns unreferenced schema emit on, for one message.</summary>
    protected abstract string EmitUnreferencedSchemasProperty { get; }

    /// <summary>A task-level default a spec item may override either way.</summary>
    private static bool Overridden(ITaskItem spec, string name, bool fallback) {
        var declared = spec.GetMetadata(name);

        return string.IsNullOrWhiteSpace(declared)
            ? fallback
            : string.Equals(declared, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Narrows a description to what one service implements. Returns true if that failed.
    /// </summary>
    /// <remarks>
    /// The filter is per-spec metadata rather than a task property, because one description is
    /// routinely sliced differently by several projects - which is the point of slicing it at all.
    /// It runs even with no filter, because dropping what nothing references is not filtering: an
    /// unreferenced schema is not part of the description any operation describes.
    /// </remarks>
    private bool Slice(ITaskItem spec, string path, ServiceSpecModel model, out bool failed) {
        failed = false;
        var filter = new SpecSlicer.Filter {
            IncludePaths = SplitMetadata(spec, "IncludePaths"),
            ExcludePaths = SplitMetadata(spec, "ExcludePaths"),
            Tags = SplitMetadata(spec, "Tags")
        };

        if (filter.IsEmpty && EmitUnreferencedSchemas) {
            return false;
        }

        var result = SpecSlicer.Apply(model, filter, EmitUnreferencedSchemas);

        if (result.MatchedNothing) {
            failed = true;

            Log.LogError(null, DiagnosticPrefix + "007", null, path, 0, 0, 0, 0,
                "The slice of '{0}' selected no operations, so nothing would be generated. " +
                "IncludePaths='{1}' ExcludePaths='{2}' Tags='{3}'.",
                path, spec.GetMetadata("IncludePaths"), spec.GetMetadata("ExcludePaths"),
                spec.GetMetadata("Tags"));

            return true;
        }

        // Should be unreachable: the closure keeps everything a surviving operation reaches. A hole
        // in it would otherwise degrade to JsonElement without saying so.
        foreach (var dangling in result.DanglingReferences) {
            Log.LogWarning(null, DiagnosticPrefix + "008", null, path, 0, 0, 0, 0,
                "The slice of '{0}' removed a schema that is still referenced: {1}. The reference " +
                "degrades to JsonElement.", path, dangling);
        }

        // Said out loud rather than done quietly: a type someone expected and did not get is the
        // failure mode here, and the count is the first thing they would want to see.
        if (filter.IsEmpty) {
            if (result.SchemasDropped > 0) {
                Log.LogMessage(MessageImportance.Normal,
                    "'{0}' declares {1} schemas no operation references; they were not generated. " +
                    "Set {2}=true to generate them.",
                    path, result.SchemasDropped, EmitUnreferencedSchemasProperty);
            }
        } else {
            Log.LogMessage(MessageImportance.Normal,
                "Sliced '{0}' to {1} operations ({2} dropped) and {3} schemas ({4} dropped).",
                path, result.OperationsKept, result.OperationsDropped,
                result.SchemasKept, result.SchemasDropped);
        }

        // Whether the served document now describes operations nobody implements, which is the
        // only thing the answer is used for - see ServedDocument and HOAT009/HSMT009.
        //
        // It used to be an unconditional true, which made the warning fire on every spec that
        // embeds its document and does not set EmitUnreferencedSchemas - the default on both
        // counts. Apply still runs in that case, to prune schemas no operation references, but
        // pruning a schema drops no operation, so the document continues to describe exactly what
        // is implemented. Both in-repo spec fixtures warned on every build, which is the warning
        // saying nothing rather than the fixtures being wrong.
        return result.OperationsDropped > 0;
    }

    /// <summary>
    /// The document the application will serve, or nothing.
    /// </summary>
    /// <remarks>
    /// Embedding the source text verbatim is what makes a served contract exact. That argument holds
    /// while the application implements the whole document; a slice implements part of it, so the
    /// original would advertise operations that answer 404 and schemas no handler can produce. Asked
    /// for anyway, it is emitted and said out loud.
    /// </remarks>
    private string ServedDocument(ITaskItem spec, string path, string document, bool sliced) {
        if (!Embedded(spec)) {
            return "";
        }

        if (sliced) {
            Log.LogWarning(null, DiagnosticPrefix + "009", null, path, 0, 0, 0, 0,
                "'{0}' is sliced but its document is embedded whole, so the application will serve " +
                "a description of operations it does not implement.", path);
        }

        return document;
    }

    /// <summary>
    /// Where this spec is served and where its reference page is, from the item that declared it.
    /// Returns false when the pair is contradictory and the spec should be skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the item rather than on an attribute because a specification-first application's contract
    /// is a build input: publishing it is a fact about that file, and stating it anywhere else means
    /// naming the file twice with nothing checking the two agree. It sits beside <c>Slice</c>,
    /// <c>Tags</c> and <c>EmbedDocument</c>, which are facts about the same file for the same reason.
    /// </para>
    /// <para>
    /// Both are absent by default. Embedding a document is not publishing it - what the application
    /// serves is a decision about public surface, and it stays one.
    /// </para>
    /// </remarks>
    private bool Published(ITaskItem spec, string path, ServiceSpecModel model) {
        var publishUrl = spec.GetMetadata("PublishUrl").Trim();
        var uiUrl = spec.GetMetadata("UiUrl").Trim();

        // A page renders exactly one document and fetches it by URL, so a page without one is a
        // page that renders an error. Said here rather than left to be discovered in a browser.
        if (uiUrl.Length > 0 && publishUrl.Length == 0) {
            Log.LogError(null, DiagnosticPrefix + "010", null, path, 0, 0, 0, 0,
                "'{0}' sets UiUrl but not PublishUrl, so the page at '{1}' would have no document " +
                "to render. Set PublishUrl to where the document should be served.", path, uiUrl);

            return false;
        }

        if (publishUrl.Length > 0 && !Embedded(spec)) {
            Log.LogError(null, DiagnosticPrefix + "011", null, path, 0, 0, 0, 0,
                "'{0}' sets PublishUrl but EmbedDocument is off, so there is no document to serve. " +
                "Remove EmbedDocument or drop PublishUrl.", path);

            return false;
        }

        model.PublishUrl = Absolute(publishUrl);
        model.UiUrl = Absolute(uiUrl);
        model.UiEnvironments = spec.GetMetadata("UiEnvironments").Trim();

        return true;
    }

    /// <summary>A URL always begins with a slash, so one written without gets one.</summary>
    private static string Absolute(string url) =>
        url.Length == 0 || url[0] == '/' ? url : "/" + url;

    private bool Embedded(ITaskItem spec) => Overridden(spec, "EmbedDocument", EmbedDocument);

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
                Log.LogError(null, DiagnosticPrefix + "001", null, path, 0, 0, 0, 0,
                    "{0} '{1}' does not exist.", SpecNoun, path);
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
                    Log.LogError(null, DiagnosticPrefix + "002", null, path, 0, 0, 0, 0,
                        "{0} '{1}' could not be parsed{2}", SpecNoun, path,
                        readerDiagnostics.Count > 0
                            ? ": " + string.Join("; ", readerDiagnostics.ToArray())
                            : ", and the reader gave no reason.");
                    continue;
                }

                // Not fatal - the document produced a model. Reported so a partially understood
                // spec does not look like a fully understood one.
                foreach (var diagnostic in readerDiagnostics) {
                    Log.LogWarning(null, DiagnosticPrefix + "006", null, path, 0, 0, 0, 0,
                        "{0} '{1}': {2}", SpecNoun, path, diagnostic);
                }

                model = parsed;
            } catch (Exception exception) {
                // The file and line belong to the spec, not to this task: the author edits the
                // document, and a build error that points at an MSBuild target instead is noise.
                Log.LogError(null, DiagnosticPrefix + "002", null, path, 0, 0, 0, 0,
                    "{0} '{1}' could not be parsed: {2}", SpecNoun, path, exception.Message);
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

            if (!Published(spec, path, model)) {
                continue;
            }

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
    /// Everything that follows from the description alone, as one file.
    /// </summary>
    /// <remarks>
    /// One file per description rather than one per type, which is what a Roslyn generator would
    /// have to do - it addresses its output by hint name and every name must be unique. On disk
    /// there is no such rule, and a single file is what keeps the target's incrementality honest:
    /// its outputs are then derivable from the item list without reading a description, which one
    /// file per type is not.
    /// </remarks>
    private string Emit(ServiceSpecModel model, string document, string specPath) =>
        SpecFileEmitter.Emit(
            model, Namespace, ExcludeFromCoverage, document, specPath, SelectedResponseModel());

    /// <summary>
    /// The mode <c>$(HardenedResponseModel)</c> names, defaulting to Standard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Union is refused rather than emitted. CSharpAuthor has no construct for a <c>union</c>
    /// declaration and no seam to write one at namespace level, so emitting it would mean
    /// string-concatenating a top-level type declaration - which is the thing this repository does
    /// not do, and the reason every emitter here goes through CSharpAuthor.
    /// </para>
    /// <para>
    /// Silently emitting the Response struct instead would be worse than refusing: the two are
    /// structurally identical, so a project that asked for Union would build, run, and answer every
    /// request correctly, while the exhaustiveness the keyword exists for was never there. That is
    /// the failure this whole design is arranged to avoid, and nothing about it looks like one.
    /// </para>
    /// <para>
    /// An unrecognised value is Standard rather than an error, because the property is typed by hand
    /// with no completion behind it and failing every build of a project that mistyped it is a worse
    /// trade than generating the interfaces it had yesterday. Union is different: it is spelled
    /// correctly and cannot be honoured.
    /// </para>
    /// </remarks>
    private SpecResponseModel SelectedResponseModel() {
        if (string.Equals(ResponseModel, "Response", System.StringComparison.OrdinalIgnoreCase)) {
            return SpecResponseModel.Response;
        }

        if (string.Equals(ResponseModel, "Union", System.StringComparison.OrdinalIgnoreCase)) {
            Log.LogError(
                "HardenedResponseModel is 'Union', which the specification-first generator cannot " +
                "emit: writing a union declaration needs a CSharpAuthor construct that does not " +
                "exist, and this generator does not compose C# as text. Use " +
                "'Response', which generates a struct of the same shape from the same case types.");

            return SpecResponseModel.Standard;
        }

        return SpecResponseModel.Standard;
    }

    /// <summary>
    /// Rewriting an unchanged file would bump its timestamp, and both the compiler's up-to-date
    /// check and the generator's incremental cache key off that - so an untouched description would
    /// invalidate the whole compilation on every build.
    /// </summary>
    private static void WriteIfChanged(string path, string content) {
        if (File.Exists(path) && File.ReadAllText(path) == content) {
            return;
        }

        File.WriteAllText(path, content);
    }
}
