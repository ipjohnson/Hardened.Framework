using System.Text;
using Hardened.Generation.Document;
using Microsoft.Build.Framework;

namespace Hardened.OpenApiDocument.BuildTask;

/// <summary>
/// Writes the OpenAPI document an assembly serves to a file, for <c>&lt;HardenedOpenApiOutput&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Runs after <c>CoreCompile</c> over the intermediate assembly. The document is the one the
/// application serves - the compact literal <c>OpenApiDocumentSource</c> embedded, found and read by
/// <see cref="ServedDocumentReader"/> without loading anything - and the file is that document in
/// the format the destination's extension asks for: indented JSON for <c>.json</c>, YAML for
/// <c>.yaml</c> and <c>.yml</c>. <c>&lt;HardenedOpenApiOutputVersion&gt;</c> lowers the file to 3.0.0
/// or 3.1.0 through <see cref="OpenApiDocumentLowering"/> for a reader that refuses the 3.2 banner;
/// what the application serves is untouched either way.
/// </para>
/// <para>
/// Written only when the content differs from what is on disk. The file is meant to be committed,
/// and a multi-targeted project runs this once per framework over identical documents; rewriting
/// identical bytes would make every build a change to review.
/// </para>
/// <para>
/// Every failure is a diagnostic under the front end's prefix - <c>HRDOA</c> for code-first,
/// <c>HOAT</c> and <c>HSMT</c> for the two described front ends - with the number meaning the same
/// thing under each. The table is docs/generator-diagnostics.md.
/// </para>
/// </remarks>
public sealed class WriteOpenApiDocument : Microsoft.Build.Utilities.Task {

    /// <summary>The compiled assembly, which is <c>@(IntermediateAssembly)</c> from the targets.</summary>
    [Required]
    public string Assembly { get; set; } = "";

    /// <summary>Where to write, as the project spelled it.</summary>
    [Required]
    public string Output { get; set; } = "";

    /// <summary>
    /// What a relative <see cref="Output"/> is relative to. The task host's working directory is
    /// not the project's, so the targets pass <c>$(MSBuildProjectDirectory)</c>.
    /// </summary>
    public string ProjectDirectory { get; set; } = "";

    /// <summary>
    /// <c>&lt;HardenedOpenApiOutputVersion&gt;</c>: empty to write the served document's version,
    /// or 3.0.0 or 3.1.0 to lower the file.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// The code prefix of the front end that wrote the document: <c>HRDOA</c>, <c>HOAT</c> or
    /// <c>HSMT</c>. Decides which fix a missing document names as well as which code it carries.
    /// </summary>
    public string DiagnosticPrefix { get; set; } = "HRDOA";

    /// <summary>The file, as an absolute path.</summary>
    [Output]
    public string WrittenPath { get; set; } = "";

    /// <summary>Whether the file's content changed.</summary>
    [Output]
    public bool Changed { get; set; }

    /// <summary>The number each condition reports under, shared by the three prefixes.</summary>
    public const string NoDocumentCode = "018";

    public const string MoreThanOneDocumentCode = "019";

    public const string UnknownExtensionCode = "028";

    public const string UnknownVersionCode = "029";

    public const string StreamLostItemSchemaCode = "030";

    private const string PublishingAttribute = "[Enable<OpenApiDocumentPublishing>]";

    public override bool Execute() {
        var prefix = string.IsNullOrWhiteSpace(DiagnosticPrefix) ? "HRDOA" : DiagnosticPrefix.Trim();
        var outputPath = ResolveOutput();
        var assemblyName = Path.GetFileName(Assembly);

        WrittenPath = outputPath;

        var yaml = IsYaml(outputPath);

        if (!yaml && !HasExtension(outputPath, ".json")) {
            Error(prefix + UnknownExtensionCode,
                $"<HardenedOpenApiOutput> is '{Output}', whose extension names no format. " +
                "Use .json for indented JSON, or .yaml or .yml for YAML.");

            return false;
        }

        string? version = null;

        if (!string.IsNullOrWhiteSpace(Version)) {
            version = OpenApiDocumentLowering.Normalise(Version);

            if (version == null) {
                Error(prefix + UnknownVersionCode,
                    $"<HardenedOpenApiOutputVersion> is '{Version}', which is not a version the export can write. " +
                    "Use 3.0.0 or 3.1.0, or remove the property to write the version the application serves.");

                return false;
            }
        }

        IReadOnlyList<ServedDocumentReader.ServedDocument> documents;

        try {
            documents = ServedDocumentReader.Read(Assembly);
        }
        catch (ServedDocumentException failure) {
            Error(prefix + NoDocumentCode, Unreadable(assemblyName, failure.EntryPoint, failure.Message));

            return false;
        }

        if (documents.Count == 0) {
            Error(prefix + NoDocumentCode, NoDocument(prefix, assemblyName));

            return false;
        }

        if (documents.Count > 1) {
            var entryPoints = string.Join(", ", documents.Select(document => document.EntryPoint));

            Error(prefix + MoreThanOneDocumentCode,
                $"{assemblyName} carries {documents.Count} served OpenAPI documents ({entryPoints}), and " +
                "<HardenedOpenApiOutput> names one file. This release exports one document per project. " +
                $"Keep one module with {PublishingAttribute} in this project and move the others to " +
                "projects of their own, or remove the property.");

            return false;
        }

        var inflated = ServedDocumentReader.Inflate(documents[0].Compressed);

        if (!StartsWith(inflated, ServedDocumentReader.ExpectedPrefix)) {
            Error(prefix + NoDocumentCode,
                Unreadable(assemblyName, documents[0].EntryPoint,
                    "the bytes under the getter do not inflate to an OpenAPI document"));

            return false;
        }

        JsonObject document;

        try {
            document = JsonTree.Parse(Encoding.UTF8.GetString(inflated)) as JsonObject
                       ?? throw new FormatException("the document is not a JSON object");
        }
        catch (FormatException failure) {
            Error(prefix + NoDocumentCode, Unreadable(assemblyName, documents[0].EntryPoint, failure.Message));

            return false;
        }

        if (version != null) {
            foreach (var operation in OpenApiDocumentLowering.Lower(document, version)) {
                Log.LogWarning(null, prefix + StreamLostItemSchemaCode, null, ProjectFile(), 0, 0, 0, 0,
                    $"'{operation}' streams its response, and OpenAPI {version} has no way to describe one - " +
                    "itemSchema arrived in 3.2. The exported file describes the operation with its media type " +
                    "and no schema. Remove <HardenedOpenApiOutputVersion> to export the 3.2 document the " +
                    "application serves.");
            }
        }

        var content = yaml ? YamlTreeWriter.Write(document) : JsonTreeWriter.WriteIndented(document);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

        if (File.Exists(outputPath) && SameBytes(outputPath, bytes)) {
            Changed = false;

            Log.LogMessage(MessageImportance.Low, $"{outputPath} already holds the served document.");

            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, bytes);

        Changed = true;

        Log.LogMessage(MessageImportance.Normal, $"Wrote the OpenAPI document {assemblyName} serves to {outputPath}.");

        return true;
    }

    private string ResolveOutput() {
        if (Path.IsPathRooted(Output)) {
            return Path.GetFullPath(Output);
        }

        var root = string.IsNullOrEmpty(ProjectDirectory) ? Directory.GetCurrentDirectory() : ProjectDirectory;

        return Path.GetFullPath(Path.Combine(root, Output));
    }

    private static bool IsYaml(string path) => HasExtension(path, ".yaml") || HasExtension(path, ".yml");

    private static bool HasExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(byte[] bytes, string prefix) {
        if (bytes.Length < prefix.Length) {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++) {
            if (bytes[index] != prefix[index]) {
                return false;
            }
        }

        return true;
    }

    private static bool SameBytes(string path, byte[] bytes) {
        var existing = File.ReadAllBytes(path);

        if (existing.Length != bytes.Length) {
            return false;
        }

        for (var index = 0; index < bytes.Length; index++) {
            if (existing[index] != bytes[index]) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What a missing document means, which depends on who should have written it. Code-first
    /// the module opted out; spec-first the generator did not run, which the front end's own
    /// 004 and 005 already describe.
    /// </summary>
    private string NoDocument(string prefix, string assemblyName) {
        var lead = $"<HardenedOpenApiOutput> is set to '{Output}', but {assemblyName} carries no served OpenAPI document. ";

        if (prefix == "HRDOA") {
            return lead +
                   "The document is written only for a module that enables publishing, and the export reads " +
                   $"that one copy. Add {PublishingAttribute} to the module that declares the routes, or " +
                   "remove the property.";
        }

        return lead +
               "A specification-first project carries one once its generator has run over the model the " +
               $"build task wrote; {prefix}004 reports the model or generated source missing and {prefix}005 " +
               "the targets imported before the specs were declared. Fix whichever of those the build " +
               "reported, or remove the property.";
    }

    private string Unreadable(string assemblyName, string entryPoint, string detail) =>
        $"<HardenedOpenApiOutput> is set to '{Output}', but the served OpenAPI document {assemblyName} carries " +
        $"under {entryPoint}.{ServedDocumentReader.DocumentTypeName}.{ServedDocumentReader.PropertyName} is not " +
        $"in a shape the export can read: {detail}. The C# compiler lowers the literal, and this build's " +
        "compiler lowered it in a shape the export does not know. Report it with the SDK version; the " +
        "fallback is a second copy of the document in an assembly attribute, which OpenApiDocumentSource " +
        "describes and this release does not ship.";

    private void Error(string code, string message) {
        Log.LogError(null, code, null, ProjectFile(), 0, 0, 0, 0, message);
    }

    private string? ProjectFile() => BuildEngine?.ProjectFileOfTaskNode;
}
