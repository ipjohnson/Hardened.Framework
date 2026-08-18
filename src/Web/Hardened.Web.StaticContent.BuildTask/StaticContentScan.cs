using System.IO.Compression;
using System.Security.Cryptography;

namespace Hardened.Web.StaticContent.BuildTask;

/// <summary>One file, as the scan found it.</summary>
public sealed record ScannedFile(
    string RoutePath,
    string RelativePath,
    string FullPath,
    string Hash,
    long Length,
    long LastModifiedUtcTicks,
    byte[]? Content,
    byte[]? GZipContent);

/// <summary>Everything a scan produced, including what it wants reported.</summary>
public sealed record ScanResult(
    IReadOnlyList<ScannedFile> Files,
    string? FallBackRoute,
    IReadOnlyList<ScanDiagnostic> Diagnostics);

public sealed record ScanDiagnostic(string Code, string Message, bool IsError);

/// <summary>
/// Walks a content directory and works out everything the runtime would otherwise discover.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the MSBuild task so it can be tested without one. The task is then the thin part
/// - read items, call this, write a file, report diagnostics - which is the only part that needs
/// MSBuild to exercise.
/// </para>
/// <para>
/// Nothing here depends on the runtime assembly. In particular it does not map an extension to a
/// content type: the manifest records the route and the runtime maps it with the same
/// <c>IFileExtToMimeTypeHelper</c> every other path uses, so there is one mime table rather than
/// two that can disagree.
/// </para>
/// </remarks>
public static class StaticContentScan {

    /// <summary>A file the build refuses to publish without being told to.</summary>
    /// <remarks>
    /// Not a security boundary - a determined author can still ship any of these - but the common
    /// case is a build step that copied a directory wholesale and nobody looked. The runtime cannot
    /// warn about it, because by the time it sees the file it is already answering a request for it.
    /// </remarks>
    private static readonly string[] SensitiveNames = [
        ".env", ".git", ".htpasswd", ".npmrc", ".pypirc", "id_rsa", "id_ed25519"
    ];

    private static readonly string[] SensitiveExtensions = [
        ".pem", ".pfx", ".key", ".p12"
    ];

    /// <summary>The names a request for a directory resolves to, in order.</summary>
    private static readonly string[] DefaultDocuments = ["index.html", "index.htm"];

    public static ScanResult Scan(
        string rootDirectory,
        string routePrefix,
        string? fallBackFile,
        long embedThreshold) {
        var diagnostics = new List<ScanDiagnostic>();
        var files = new List<ScannedFile>();

        var root = Path.GetFullPath(rootDirectory);

        if (!Directory.Exists(root)) {
            diagnostics.Add(new ScanDiagnostic(
                "HSTATIC001",
                $"The static content directory '{rootDirectory}' does not exist. " +
                "Point <HardenedStaticContent> at a directory that does, or remove it.",
                IsError: true));

            return new ScanResult(files, null, diagnostics);
        }

        var prefix = NormalisePrefix(routePrefix);

        foreach (var fullPath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal)) {
            var relative = Relative(root, fullPath);

            if (!WithinRoot(root, fullPath, out var resolvedPath)) {
                diagnostics.Add(new ScanDiagnostic(
                    "HSTATIC002",
                    $"'{relative}' resolves to '{resolvedPath}', which is outside the content " +
                    "directory. A link out of the content root would be served at run time, so it " +
                    "is refused here.",
                    IsError: true));

                continue;
            }

            if (IsSensitive(relative)) {
                diagnostics.Add(new ScanDiagnostic(
                    "HSTATIC003",
                    $"'{relative}' looks like a secret and would be served to anyone who asks for " +
                    "it. Move it out of the content directory, or silence this with <NoWarn>.",
                    IsError: false));
            }

            files.Add(Describe(root, fullPath, relative, prefix, embedThreshold));
        }

        if (files.Count == 0) {
            diagnostics.Add(new ScanDiagnostic(
                "HSTATIC004",
                $"The static content directory '{rootDirectory}' is empty, so the application " +
                "serves no files. Remove the <HardenedStaticContent> item, or put something in it.",
                IsError: false));
        }

        var byRoute = files.ToDictionary(file => file.RoutePath, StringComparer.Ordinal);

        AddDefaultDocuments(files, byRoute);

        var fallBackRoute = ResolveFallBack(fallBackFile, prefix, byRoute, diagnostics);

        return new ScanResult(files, fallBackRoute, diagnostics);
    }

    /// <summary>
    /// Aliases a directory onto its default document, so <c>/assets/</c> answers rather than 404s.
    /// </summary>
    /// <remarks>
    /// Resolved here rather than probed per request, which is what makes a plain static site
    /// expressible at all: without it the only way to answer <c>/</c> was the single-page fall
    /// back, which then answered every unknown path too.
    /// </remarks>
    private static void AddDefaultDocuments(
        List<ScannedFile> files, Dictionary<string, ScannedFile> byRoute) {
        var aliases = new List<ScannedFile>();

        // Grouped by directory and then chosen by preference, not by whichever file the walk
        // reached first. A directory holding both index.html and index.htm has to resolve the same
        // way every time, and the order the file system hands them over is not that.
        var byDirectory = new Dictionary<string, List<ScannedFile>>(StringComparer.Ordinal);

        foreach (var file in files) {
            var name = Path.GetFileName(file.RoutePath);

            if (!DefaultDocuments.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            var directoryRoute = file.RoutePath.Substring(0, file.RoutePath.Length - name.Length);

            if (!byDirectory.TryGetValue(directoryRoute, out var candidates)) {
                byDirectory[directoryRoute] = candidates = new List<ScannedFile>();
            }

            candidates.Add(file);
        }

        // .Key/.Value rather than deconstruction: KeyValuePair does not deconstruct on net472,
        // and the task targets it because Visual Studio's MSBuild runs there.
        foreach (var directory in byDirectory) {
            var directoryRoute = directory.Key;

            var chosen = directory.Value
                .OrderBy(file => Array.FindIndex(
                    DefaultDocuments,
                    document => string.Equals(
                        document, Path.GetFileName(file.RoutePath), StringComparison.OrdinalIgnoreCase)))
                .First();

            // Both spellings of the directory. The one without the slash matters because that is
            // what a client links to; the one with it is what a relative reference inside the
            // document resolves against.
            foreach (var alias in new[] { directoryRoute, directoryRoute.TrimEnd('/') }) {
                var route = alias.Length == 0 ? "/" : alias;

                // Never over a file that is really there. A route an actual file occupies is not
                // one to invent an alias for.
                if (byRoute.ContainsKey(route)) {
                    continue;
                }

                var aliased = chosen with { RoutePath = route };

                byRoute[route] = aliased;
                aliases.Add(aliased);
            }
        }

        files.AddRange(aliases);
    }

    private static string? ResolveFallBack(
        string? fallBackFile,
        string prefix,
        Dictionary<string, ScannedFile> byRoute,
        List<ScanDiagnostic> diagnostics) {
        if (string.IsNullOrWhiteSpace(fallBackFile)) {
            return null;
        }

        var route = prefix + fallBackFile!.TrimStart('/');

        if (byRoute.ContainsKey(route)) {
            return route;
        }

        diagnostics.Add(new ScanDiagnostic(
            "HSTATIC005",
            $"The fall back file '{fallBackFile}' is not in the content directory, so every " +
            "unknown path would fail at run time rather than serve the application shell.",
            IsError: true));

        return null;
    }

    private static ScannedFile Describe(
        string root, string fullPath, string relative, string prefix, long embedThreshold) {
        var bytes = File.ReadAllBytes(fullPath);

        byte[]? content = null;
        byte[]? gzip = null;

        if (bytes.LongLength <= embedThreshold) {
            content = bytes;

            var compressed = Compress(bytes);

            // Kept only when it actually helps. Compressing an already-compressed format - a PNG, a
            // woff2 - reliably produces something larger, and shipping that costs assembly size to
            // make the response bigger.
            if (compressed.Length < bytes.Length) {
                gzip = compressed;
            }
        }

        return new ScannedFile(
            RoutePath: prefix + relative.Replace(Path.DirectorySeparatorChar, '/'),
            RelativePath: relative,
            FullPath: fullPath,
            Hash: Hash(bytes),
            Length: bytes.LongLength,
            LastModifiedUtcTicks: File.GetLastWriteTimeUtc(fullPath).Ticks,
            Content: content,
            GZipContent: gzip);
    }

    /// <remarks>
    /// SHA-256 rather than MD5. The value is opaque and any hash would serve as a validator, but
    /// MD5 throws outright on a FIPS-enforcing host - and doing it here rather than at run time
    /// means that cannot happen at all.
    /// </remarks>
    private static string Hash(byte[] bytes) {
        using var sha = SHA256.Create();

        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    /// <remarks>
    /// <c>Optimal</c> rather than <c>SmallestSize</c>, which netstandard and net472 do not have. On
    /// text the two are within a few percent, and this runs inside the build.
    /// </remarks>
    private static byte[] Compress(byte[] bytes) {
        using var output = new MemoryStream();

        // Disposed before the buffer is read: GZipStream writes its footer on dispose, so a buffer
        // taken while it is still open holds a truncated member that inflates to nothing.
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Whether the file a path names is really inside the root.
    /// </summary>
    /// <remarks>
    /// <c>Path.GetFullPath</c> normalises lexically and does not follow links, so a symlink sitting
    /// inside the content directory and pointing outside it passes every containment check the
    /// runtime can make and is served. Resolving it here is what makes that impossible rather than
    /// merely unlikely.
    /// </remarks>
    private static bool WithinRoot(string root, string fullPath, out string resolved) {
        resolved = fullPath;

        try {
            var target = FrameworkCompat.ResolveLinkTarget(fullPath);

            if (target != null) {
                resolved = target;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            // Unreadable link. Treated as escaping, because what it points at cannot be checked.
            return false;
        }

        var relative = FrameworkCompat.GetRelativePath(root, resolved);

        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static bool IsSensitive(string relative) {
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var segment in segments) {
            foreach (var name in SensitiveNames) {
                if (string.Equals(segment, name, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }

        var extension = Path.GetExtension(relative);

        foreach (var sensitive in SensitiveExtensions) {
            if (string.Equals(extension, sensitive, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string Relative(string root, string fullPath) =>
        FrameworkCompat.GetRelativePath(root, fullPath);

    /// <summary>The prefix every route under this mount carries, with both slashes settled.</summary>
    private static string NormalisePrefix(string routePrefix) {
        if (string.IsNullOrWhiteSpace(routePrefix) || routePrefix == "/") {
            return "/";
        }

        var prefix = routePrefix.Trim();

        if (!prefix.StartsWith("/", StringComparison.Ordinal)) {
            prefix = "/" + prefix;
        }

        return prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
    }
}
