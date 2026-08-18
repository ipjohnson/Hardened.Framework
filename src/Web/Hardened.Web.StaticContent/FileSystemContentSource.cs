using System.Collections.Concurrent;
using System.IO.Compression;
using DependencyModules.Runtime.Attributes;
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
/// The behaviour <c>StaticContentHandler</c> had, extracted so that the pipeline above it stops
/// depending on it. Files are found by resolving the request path against the configured root and
/// asking the filesystem; an entry is built on first request and reused after that.
/// </para>
/// <para>
/// <b>Or not kept, when <see cref="IStaticContentConfiguration.CacheContent"/> is off.</b> Every
/// request then stats and reads, which is what makes a developer's edit visible on reload without a
/// watcher, a change token or any invalidation at all.
/// </para>
/// <para>
/// <b>What is deliberately not here.</b> Nothing bounds the cache and nothing prevents two
/// concurrent first-requests for one file from both reading and compressing it. Both are real and
/// both are answered by a manifest computed at build time rather than by more machinery here.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class FileSystemContentSource : IStaticContentSource {
    private const string GzFileExtension = ".gz";
    private const string BrFileExtension = ".br";

    /// <summary>
    /// Below this, compressing costs more than it saves and the client inflates for nothing.
    /// </summary>
    private const int CompressionThreshold = 1000;

    private readonly ILogger<FileSystemContentSource> _logger;
    private readonly IStaticContentConfiguration _configuration;
    private readonly IGZipStaticContentCompressor _compressor;
    private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
    private readonly IETagProvider _etagProvider;
    private readonly ConcurrentDictionary<string, StaticContentEntry> _entries = new();
    private readonly string _rootPath;

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
    }

    public bool Enabled { get; }

    public StaticContentLocation? Locate(string requestPath) =>
        Enabled ? Locate(requestPath, viaFallback: false) : null;

    public async ValueTask<StaticContentEntry?> Load(StaticContentLocation location) {
        if (location.Cached != null) {
            return location.Cached;
        }

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

        var entry = location.ContentEncoding != null
            ? new StaticContentEntry(
                contentType, location.ContentEncoding, isBinary,
                _etagProvider.GenerateETag(fileBytes), fileBytes, lastModified)
            : BuildEntry(contentType, isBinary, fileBytes, lastModified);

        if (_configuration.CacheContent) {
            _entries.AddOrUpdate(location.Key, _ => entry, (_, _) => entry);
        }

        return entry;
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

        // Compression is paid once on the way into the cache and recovered on every request after
        // it. With no cache there is no "after", so it would be paid on every request at the level
        // that produces the smallest result - which is the slowest one there is, and the wrong
        // trade for a browser on localhost.
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
    /// The file behind <paramref name="requestPath"/>: the path as written, one of its
    /// pre-compressed siblings, or the fall back file.
    /// </summary>
    private StaticContentLocation? Locate(string requestPath, bool viaFallback) {
        var filePath = ResolveWithinRoot(requestPath);

        // Outside the configured root - refuse before touching the filesystem or the cache.
        if (filePath == null) {
            return null;
        }

        // Keyed on the resolved file, not on the request path. Those differ for every request the
        // fall back file answers, and keying on the request meant a distinct entry - holding its own
        // complete copy of the shell - for every URL a client asked for. One file, one entry.
        // Skipped outright rather than left to miss: with caching off nothing is ever written, so
        // the lookup could only ever cost a hash of the path and return nothing.
        if (_configuration.CacheContent && _entries.TryGetValue(filePath, out var cached)) {
            return new StaticContentLocation(
                filePath, filePath, cached.ContentEncoding, cached, viaFallback);
        }

        if (File.Exists(filePath)) {
            return new StaticContentLocation(filePath, filePath, null, null, viaFallback);
        }

        if (File.Exists(filePath + GzFileExtension)) {
            return new StaticContentLocation(
                filePath, filePath + GzFileExtension, KnownEncoding.GZip, null, viaFallback);
        }

        if (File.Exists(filePath + BrFileExtension)) {
            return new StaticContentLocation(
                filePath, filePath + BrFileExtension, KnownEncoding.Br, null, viaFallback);
        }

        if (viaFallback || string.IsNullOrEmpty(_configuration.FallBackFile)) {
            return null;
        }

        var fallback = Locate(_configuration.FallBackFile!, viaFallback: true);

        if (fallback == null) {
            // Raised rather than declined, and on the first request rather than at startup because
            // that is when the path is first walked. Returning null would turn every unknown path
            // into a 404 and hide the misconfiguration completely.
            throw new Exception("Service is misconfigured, cannot find static fall back file: " +
                                _configuration.FallBackFile);
        }

        return fallback;
    }

    /// <summary>
    /// Maps a request path onto the filesystem and confirms the result is still inside the
    /// configured root, returning null when it is not.
    ///
    /// Path.Combine does not resolve traversal sequences, so "/../secret" combined with the
    /// root points outside it. Hardened is transport agnostic and not every transport
    /// normalises the request path the way Kestrel does - API Gateway delivers RawPath - so
    /// the source cannot assume that has already happened.
    /// </summary>
    private string? ResolveWithinRoot(string requestPath) {
        string candidate;

        try {
            candidate = Path.GetFullPath(Path.Combine(_rootPath, requestPath.TrimStart('/')));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException) {
            // Malformed path - treat exactly like one that escapes the root.
            return null;
        }

        var relative = Path.GetRelativePath(_rootPath, candidate);

        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative)) {
            _logger.LogWarning(
                "Static content request {RequestPath} resolved outside the configured root and was refused",
                requestPath);

            return null;
        }

        return candidate;
    }
}
