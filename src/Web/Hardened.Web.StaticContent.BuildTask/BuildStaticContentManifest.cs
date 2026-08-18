using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Hardened.Web.StaticContent.BuildTask;

/// <summary>
/// Scans the content directory and writes the manifest the application compiles.
/// </summary>
/// <remarks>
/// <para>
/// A build task rather than a source generator, and not by preference: a generator sees only
/// <c>AdditionalFiles</c> and reads them as text, so it can neither enumerate a directory nor carry
/// a PNG through without corrupting it. MSBuild runs first, so a file it puts into
/// <c>@(Compile)</c> is indistinguishable from one a human wrote.
/// </para>
/// <para>
/// Thin on purpose. Everything worth testing is in <see cref="StaticContentScan"/> and
/// <see cref="ManifestEmitter"/>, which need no MSBuild to exercise; what is left here is reading
/// items, writing a file, and turning diagnostics into log entries.
/// </para>
/// </remarks>
public class BuildStaticContentManifest : Microsoft.Build.Utilities.Task {

    /// <summary>The directory to scan.</summary>
    [Required]
    public string ContentDirectory { get; set; } = string.Empty;

    /// <summary>Where the generated C# goes. Declared to the compilation at evaluation time.</summary>
    [Required]
    public string OutputFile { get; set; } = string.Empty;

    /// <summary>The namespace the manifest class lands in.</summary>
    public string Namespace { get; set; } = "Generated";

    /// <summary>The path prefix every route under this mount carries.</summary>
    public string RoutePrefix { get; set; } = "/";

    /// <summary>The file that answers a path with nothing behind it.</summary>
    public string? FallBackFile { get; set; }

    /// <summary>
    /// Files at or under this many bytes travel in the assembly; larger ones are read from disk.
    /// </summary>
    /// <remarks>
    /// Embedding makes assembly size a function of asset size, which is the right trade for a
    /// single-page shell and the wrong one for a video. A megabyte is the line because it is about
    /// where an asset stops being something every request wants.
    /// </remarks>
    public long EmbedThresholdBytes { get; set; } = 1024 * 1024;

    /// <summary>Whether to keep generated code out of the coverage numbers.</summary>
    public bool ExcludeFromCoverage { get; set; } = true;

    /// <summary>What was written, so the target can report it.</summary>
    [Output]
    public string? GeneratedSource { get; set; }

    public override bool Execute() {
        ScanResult scan;

        try {
            scan = StaticContentScan.Scan(
                ContentDirectory, RoutePrefix, FallBackFile, EmbedThresholdBytes);
        }
        catch (Exception exception) {
            // A failure reading the tree is the build's problem, not something to half-answer with
            // an empty manifest that would silently serve nothing.
            // Named arguments, because the first parameter of this overload is the subcategory
            // and the second is the code. Passing the code positionally puts it in the subcategory,
            // where MSBuild does not read it - so the diagnostic prints without an identifier and
            // <NoWarn> has nothing to match on.
            Log.LogError(
                subcategory: null, errorCode: "HSTATIC000", helpKeyword: null, file: null,
                lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: "Could not read the static content directory '{0}': {1}",
                ContentDirectory, exception.Message);

            return false;
        }

        var failed = false;

        foreach (var diagnostic in scan.Diagnostics) {
            if (diagnostic.IsError) {
                Log.LogError(
                    subcategory: null, errorCode: diagnostic.Code, helpKeyword: null,
                    file: ContentDirectory, lineNumber: 0, columnNumber: 0,
                    endLineNumber: 0, endColumnNumber: 0,
                    message: "{0}", diagnostic.Message);

                failed = true;
            }
            else {
                Log.LogWarning(
                    subcategory: null, warningCode: diagnostic.Code, helpKeyword: null,
                    file: ContentDirectory, lineNumber: 0, columnNumber: 0,
                    endLineNumber: 0, endColumnNumber: 0,
                    message: "{0}", diagnostic.Message);
            }
        }

        if (failed) {
            return false;
        }

        var source = ManifestEmitter.Emit(scan, Namespace, ExcludeFromCoverage);

        var directory = Path.GetDirectoryName(OutputFile);

        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory!);
        }

        // Only when it differs. The file is an input to the compilation, and rewriting it with
        // identical bytes moves its timestamp and makes every build a full rebuild.
        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != source) {
            File.WriteAllText(OutputFile, source);
        }

        GeneratedSource = OutputFile;

        Log.LogMessage(
            MessageImportance.Normal,
            "Static content manifest: {0} file(s) from '{1}'.", scan.Files.Count, ContentDirectory);

        return true;
    }
}
