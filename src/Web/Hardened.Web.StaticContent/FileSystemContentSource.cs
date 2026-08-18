using System.Collections.Concurrent;
using System.IO.Compression;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Content read from a directory on disk, on first request, and kept.
/// </summary>
/// <remarks>
/// <para>
/// The source an application gets when it declares no <c>&lt;HardenedStaticContent&gt;</c> build
/// items. It discovers per request what <see cref="ManifestContentSource"/> is handed - which file
/// answers a path, what its hash is, whether it is worth compressing, whether a link escapes the
/// root - so every protection the build task applies has to be applied here too, or an application
/// that has not opted into the build task is running without them.
/// </para>
/// <para>
/// <b>What is still not here.</b> Nothing bounds the cache: an application serving a large tree
/// holds all of it once every file has been asked for. That is the one thing a manifest answers and
/// this cannot, because it only ever learns about a file by being asked for it.
/// </para>
/// </remarks>
/// <remarks>
/// <b>Deliberately not <c>[SingletonService]</c>.</b> Both sources implement
/// <c>IStaticContentSource</c>, so auto-registering them registered two implementations of one
/// interface with <c>TryAdd</c> - and the first one emitted won, silently, whatever the application
/// had. Which source answers is a decision, made once in
/// <c>HardenedStaticContent.ConfigureServices</c> from whether the build produced a manifest.
/// </remarks>
public class FileSystemContentSource : IStaticContentSource {
    private const string GzFileExtension = ".gz";
    private const string BrFileExtension = ".br";

    /// <summary>
    /// Below this, compressing costs more than it saves and the client inflates for nothing.
    /// </summary>
    private const int CompressionThreshold = 1000;

    /// <summary>The names a request for a directory resolves to, in order.</summary>
    private static readonly string[] DefaultDocuments = ["index.html", "index.htm"];

    /// <summary>
    /// The one hidden directory that is meant to be served.
    /// </summary>
    /// <remarks>
    /// ACME challenges, <c>security.txt</c> and every other well-known URI live under it. Refusing
    /// hidden paths without this exception breaks certificate renewal, which is a failure nobody
    /// connects back to a static content setting.
    /// </remarks>
    private const string WellKnown = ".well-known";

    private readonly ILogger<FileSystemContentSource> _logger;
    private readonly IStaticContentConfiguration _configuration;
    private readonly IGZipStaticContentCompressor _compressor;
    private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
    private readonly IETagProvider _etagProvider;

    /// <summary>
    /// Keyed on the resolved file. <c>Lazy</c> rather than the entry itself, so that concurrent
    /// first-requests for one file share a single read: <c>ConcurrentDictionary.GetOrAdd</c> does
    /// not promise its factory runs once, and without that every request arriving before the first
    /// finished read the file and compressed it again, at the level that produces the smallest
    /// result.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<StaticContentEntry?>>> _entries = new();

    private readonly string _rootPath;
    private readonly string? _fallBackFile;

    public FileSystemContentSource(
        IOptions<IStaticContentConfiguration> configuration,
        IFileExtToMimeTypeHelper fileExtToMimeTypeHelper,
        IGZipStaticContentCompressor compressor,
        IETagProvider etagProvider,
        ILogger<FileSystemContentSource> logger) {
        _fileExtToMimeTypeHelper = fileExtToMimeTypeHelper;
        _compressor = compressor;
        _etagProvider = etagProvider;
        _logger = logger;
        _configuration = configuration.Value;

        // Fully qualified so that containment checks in ResolveWithinRoot compare canonical paths on
        // both sides.
        _rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _configuration.Path));
        Enabled = Directory.Exists(_rootPath);

        if (!Enabled) {
            // AppContext.BaseDirectory rather than Assembly.Location, which is an empty string for
            // an assembly inside a single-file or AOT-published application. So the fallback that
            // exists for a process whose working directory is not its deployment directory was the
            // one thing that stopped working in the deployments most likely to have one.
            var baseDirectory = AppContext.BaseDirectory;

            if (!string.IsNullOrEmpty(baseDirectory)) {
                _rootPath = Path.GetFullPath(Path.Combine(baseDirectory, _configuration.Path));
                Enabled = Directory.Exists(_rootPath);
            }
        }

        WarnIfRootIsSuspicious();

        _fallBackFile = ResolveFallBackFile();
    }

    public bool Enabled { get; }

    public StaticContentLocation? Locate(string requestPath) =>
        Enabled ? Locate(requestPath, viaFallback: false) : null;

    public async ValueTask<StaticContentEntry?> Load(StaticContentLocation location) {
        if (location.Cached != null) {
            return location.Cached;
        }

        if (!_configuration.CacheContent) {
            return await Read(location);
        }

        var lazy = _entries.GetOrAdd(
            location.Key,
            _ => new Lazy<Task<StaticContentEntry?>>(
                () => Read(location), LazyThreadSafetyMode.ExecutionAndPublication));

        var entry = await lazy.Value;

        if (entry == null) {
            // Never cache a failure. The file was there when Locate looked, so this is a deletion or
            // a transient read error, and pinning it would answer 404 for a file that came back.
            _entries.TryRemove(location.Key, out _);
        }

        return entry;
    }

    private async Task<StaticContentEntry?> Read(StaticContentLocation location) {
        byte[] fileBytes;

        try {
            fileBytes = await File.ReadAllBytesAsync(location.FilePath);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException) {
            // Deleted between Locate and here. A race rather than a mistake, and the caller turns it
            // into the same answer the path would have got had Locate seen it gone.
            return null;
        }

        var (contentType, isBinary) =
            _fileExtToMimeTypeHelper.GetMimeTypeInfo(Path.GetExtension(location.Key));

        // Read after the bytes rather than before: a file rewritten between the two would otherwise
        // be served with a timestamp older than its contents, and a cache holding it would never
        // revalidate.
        DateTimeOffset? lastModified;

        try {
            lastModified = File.GetLastWriteTimeUtc(location.FilePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            lastModified = null;
        }

        if (location.ContentEncoding != null) {
            return new StaticContentEntry(
                contentType, location.ContentEncoding, isBinary,
                _etagProvider.GenerateETag(fileBytes), fileBytes, lastModified);
        }

        return BuildEntry(contentType, isBinary, fileBytes, lastModified);
    }

    /// <summary>
    /// An entry for a file stored as itself, compressed on the way in when that is worth doing.
    /// </summary>
    /// <remarks>
    /// The validator is taken from the bytes as read rather than as stored, so it names the resource
    /// and the compressed representation derives its own tag from it. Compression happens once, on
    /// the way into the cache, at the level that produces the smallest result - the cost is paid on
    /// one request and recovered on every one after it.
    /// </remarks>
    private StaticContentEntry BuildEntry(
        string contentType, bool isBinary, byte[] fileBytes, DateTimeOffset? lastModified) {
        var hash = _etagProvider.GenerateETag(fileBytes);

        // Compression is paid once into a cache and recovered on every request after it. With no
        // cache there is no "after", so it would be paid on every request at the level that
        // produces the smallest result - the wrong trade for a browser on localhost.
        if (!_configuration.CacheContent ||
            !_configuration.CompressTextContent || isBinary || fileBytes.Length <= CompressionThreshold) {
            return new StaticContentEntry(
                contentType, null, isBinary, hash, fileBytes, lastModified);
        }

        return new StaticContentEntry(
            contentType, KnownEncoding.GZip, isBinary, hash,
            _compressor.CompressContent(fileBytes, CompressionLevel.SmallestSize), lastModified);
    }

    /// <summary>
    /// The file behind <paramref name="requestPath"/>: the path as written, its default document,
    /// one of its pre-compressed siblings, or the fall back file.
    /// </summary>
    private StaticContentLocation? Locate(string requestPath, bool viaFallback) {
        var filePath = ResolveWithinRoot(requestPath);

        // Outside the configured root - refuse before touching the filesystem or the cache.
        if (filePath == null) {
            return FallBack(viaFallback);
        }

        // Skipped outright when caching is off rather than left to miss: nothing is ever written,
        // so the lookup could only cost a hash of the path and return nothing.
        if (_configuration.CacheContent &&
            _entries.TryGetValue(filePath, out var lazy) &&
            lazy.IsValueCreated &&
            lazy.Value is { IsCompletedSuccessfully: true, Result: { } cached }) {
            return new StaticContentLocation(
                filePath, filePath, cached.ContentEncoding, cached, viaFallback);
        }

        if (Servable(filePath)) {
            return new StaticContentLocation(filePath, filePath, null, null, viaFallback);
        }

        if (Servable(filePath + GzFileExtension)) {
            return new StaticContentLocation(
                filePath, filePath + GzFileExtension, KnownEncoding.GZip, null, viaFallback);
        }

        if (Servable(filePath + BrFileExtension)) {
            return new StaticContentLocation(
                filePath, filePath + BrFileExtension, KnownEncoding.Br, null, viaFallback);
        }

        var defaultDocument = DefaultDocumentIn(filePath);

        if (defaultDocument != null) {
            return new StaticContentLocation(
                defaultDocument, defaultDocument, null, null, viaFallback);
        }

        return FallBack(viaFallback);
    }

    private StaticContentLocation? FallBack(bool viaFallback) =>
        viaFallback || _fallBackFile == null ? null : Locate(_fallBackFile, viaFallback: true);

    /// <summary>
    /// The default document inside <paramref name="directoryPath"/>, when it is a directory.
    /// </summary>
    /// <remarks>
    /// Resolved for both spellings of the directory, so <c>/assets</c> and <c>/assets/</c> both
    /// answer. Without it the only way to serve anything at <c>/</c> was the single-page fall back,
    /// which then answered every unknown path too - so a plain static site was not expressible.
    /// </remarks>
    private string? DefaultDocumentIn(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return null;
        }

        foreach (var document in DefaultDocuments) {
            var candidate = Path.Combine(directoryPath, document);

            if (Servable(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a request path onto the filesystem and confirms the result is a file this mount will
    /// serve, returning null when it is not.
    ///
    /// Path.Combine does not resolve traversal sequences, so "/../secret" combined with the
    /// root points outside it. Hardened is transport agnostic and not every transport
    /// normalises the request path the way Kestrel does - API Gateway delivers RawPath - so
    /// the source cannot assume that has already happened.
    /// </summary>
    private string? ResolveWithinRoot(string requestPath) {
        if (!_configuration.ServeHiddenFiles && IsHidden(requestPath)) {
            return null;
        }

        string candidate;

        try {
            candidate = Path.GetFullPath(Path.Combine(_rootPath, requestPath.TrimStart('/')));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException) {
            // Malformed path - treat exactly like one that escapes the root.
            return null;
        }

        if (!IsUnder(_rootPath, candidate)) {
            _logger.LogWarning(
                "Static content request {RequestPath} resolved outside the configured root and was refused",
                requestPath);

            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a file this mount will open: it exists, and following it
    /// stays inside the root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The link is resolved here rather than in <see cref="ResolveWithinRoot"/> because it can only
    /// be resolved for a file that exists - <c>ResolveLinkTarget</c> raises for one that does not,
    /// and a request for <c>/app.js</c> served by <c>app.js.gz</c> asks about a path that never
    /// exists. Checking each concrete candidate instead is both correct and the only place the
    /// answer matters, since this is the path about to be read.
    /// </para>
    /// <para>
    /// <c>Path.GetFullPath</c> normalises lexically and does not follow links, so a symlink sitting
    /// inside the content directory and pointing outside it passes every check made on the path
    /// alone and is served. Anything that populates the directory - an npm postinstall, a Docker
    /// layer, a CI artifact step - can create one.
    /// </para>
    /// </remarks>
    private bool Servable(string path) {
        if (!File.Exists(path)) {
            return false;
        }

        try {
            var target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);

            if (target == null || IsUnder(_rootPath, Path.GetFullPath(target.FullName))) {
                return true;
            }

            _logger.LogWarning(
                "Static content {Path} is a link to {Target}, which is outside the configured " +
                "root, and was refused", path, target.FullName);

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException) {
            // Unreadable link. Treated as escaping, because what it points at cannot be checked.
            return false;
        }
    }

    private static bool IsUnder(string root, string candidate) {
        var relative = Path.GetRelativePath(root, candidate);

        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// Whether any segment of the request is a hidden name.
    /// </summary>
    /// <remarks>
    /// <c>.env</c>, <c>.git</c> and <c>.htpasswd</c> are the reason. This is not a boundary - an
    /// author who wants them served sets <see cref="IStaticContentConfiguration.ServeHiddenFiles"/> -
    /// but the common case is a build step that copied a directory wholesale, and the framework
    /// answering that request is the last chance to not.
    /// </remarks>
    private static bool IsHidden(string requestPath) {
        foreach (var segment in requestPath.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            if (segment.Length > 1 &&
                segment[0] == '.' &&
                !string.Equals(segment, WellKnown, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The fall back file, if it is there, and a complaint if it is not.
    /// </summary>
    /// <remarks>
    /// Checked once here rather than raised on every request that reaches it. It used to throw, so
    /// a typo turned every unknown path into a 500 forever - a failure that looks like the
    /// application is broken rather than like a setting is wrong. Reported once and disabled: the
    /// build task refuses outright, which is the version of this that fails before deployment.
    /// </remarks>
    private string? ResolveFallBackFile() {
        var configured = _configuration.FallBackFile;

        if (!Enabled || string.IsNullOrEmpty(configured)) {
            return null;
        }

        var resolved = ResolveWithinRoot(configured!);

        if (resolved != null && (Servable(resolved) ||
                                 Servable(resolved + GzFileExtension) ||
                                 Servable(resolved + BrFileExtension) ||
                                 DefaultDocumentIn(resolved) != null)) {
            return configured;
        }

        _logger.LogError(
            "Static content is configured with fall back file {FallBackFile}, which is not in {Root}. " +
            "Unknown paths will answer 404 rather than the application shell",
            configured, _rootPath);

        return null;
    }

    /// <summary>
    /// Says so when the configured root is one nobody means.
    /// </summary>
    /// <remarks>
    /// <c>Path.Combine</c> returns a rooted second argument unchanged, so <c>Path = "/"</c> makes
    /// the whole filesystem the document root and every containment check passes, because nothing
    /// escapes. A root that is the application's own directory serves <c>appsettings.json</c> the
    /// same way. Neither is refused - an author may mean it - but neither should be silent.
    /// </remarks>
    private void WarnIfRootIsSuspicious() {
        if (!Enabled) {
            return;
        }

        if (Path.GetPathRoot(_rootPath) == _rootPath) {
            _logger.LogWarning(
                "Static content is rooted at {Root}, the filesystem root, so every file this " +
                "process can read is reachable over HTTP", _rootPath);

            return;
        }

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(_rootPath),
                Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                StringComparison.Ordinal)) {
            _logger.LogWarning(
                "Static content is rooted at the application's own directory {Root}, so its " +
                "configuration and assemblies are reachable over HTTP", _rootPath);
        }
    }
}
