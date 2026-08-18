using System.Collections.Concurrent;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Content the build already found, hashed and compressed.
/// </summary>
/// <remarks>
/// <para>
/// The request path is a dictionary lookup against a table fixed at build time. Nothing is
/// discovered, so nothing is unbounded: there is no cache that grows, no first request that pays
/// for a hash, and no two concurrent requests that both compress the same file. A path not in the
/// table does not exist, full stop - which is also why traversal cannot escape here. There is
/// nothing to escape to.
/// </para>
/// <para>
/// Entries are turned into responses lazily and kept, because building every one at startup would
/// pay for files a process may never serve. That is bounded by the manifest, unlike the file system
/// source's cache, which is bounded by what clients ask for.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class ManifestContentSource : IStaticContentSource {
    private readonly IStaticContentManifest _manifest;
    private readonly IStaticContentConfiguration _configuration;
    private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
    private readonly ILogger<ManifestContentSource> _logger;
    private readonly Dictionary<string, StaticContentManifestEntry> _entries;
    private readonly ConcurrentDictionary<string, StaticContentEntry> _built = new();
    private readonly string _rootPath;

    public ManifestContentSource(
        IStaticContentManifest manifest,
        IOptions<IStaticContentConfiguration> configuration,
        IFileExtToMimeTypeHelper fileExtToMimeTypeHelper,
        ILogger<ManifestContentSource> logger) {
        _manifest = manifest;
        _configuration = configuration.Value;
        _fileExtToMimeTypeHelper = fileExtToMimeTypeHelper;
        _logger = logger;

        // Ordinal, not case-insensitive. A URL path is case sensitive, and matching loosely here
        // would serve /Logo.PNG for /logo.png on Linux and not on Windows - which is how a site
        // works in development and 404s in production.
        _entries = manifest.Entries.ToDictionary(entry => entry.RoutePath, StringComparer.Ordinal);

        _rootPath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), _configuration.Path));

        if (!Directory.Exists(_rootPath)) {
            var baseDirectory = AppContext.BaseDirectory;

            if (!string.IsNullOrEmpty(baseDirectory)) {
                _rootPath = Path.GetFullPath(Path.Combine(baseDirectory, _configuration.Path));
            }
        }
    }

    /// <summary>
    /// True when the build found anything. A manifest with no entries is an application that
    /// declared a content directory and shipped nothing in it.
    /// </summary>
    public bool Enabled => _entries.Count > 0;

    public StaticContentLocation? Locate(string requestPath) {
        if (_entries.TryGetValue(requestPath, out var entry)) {
            return Location(entry, viaFallback: false);
        }

        if (_manifest.FallBackRoute != null &&
            _entries.TryGetValue(_manifest.FallBackRoute, out var fallback)) {
            return Location(fallback, viaFallback: true);
        }

        return null;
    }

    public ValueTask<StaticContentEntry?> Load(StaticContentLocation location) {
        if (location.Cached != null) {
            return new ValueTask<StaticContentEntry?>(location.Cached);
        }

        if (!_entries.TryGetValue(location.Key, out var entry)) {
            return new ValueTask<StaticContentEntry?>((StaticContentEntry?)null);
        }

        return Build(entry);
    }

    private StaticContentLocation Location(StaticContentManifestEntry entry, bool viaFallback) =>
        new(entry.RoutePath,
            entry.RelativePath ?? entry.RoutePath,
            entry.GZipContent != null ? KnownEncoding.GZip : null,
            _built.TryGetValue(entry.RoutePath, out var built) ? built : null,
            viaFallback);

    private async ValueTask<StaticContentEntry?> Build(StaticContentManifestEntry entry) {
        var (contentType, isBinary) =
            _fileExtToMimeTypeHelper.GetMimeTypeInfo(Path.GetExtension(entry.RoutePath));

        byte[] content;
        string? encoding;

        if (entry.GZipContent != null) {
            content = entry.GZipContent;
            encoding = KnownEncoding.GZip;
        }
        else if (entry.Content != null) {
            content = entry.Content;
            encoding = null;
        }
        else {
            var read = await ReadFromDisk(entry);

            if (read == null) {
                return null;
            }

            content = read;
            encoding = null;
        }

        var built = new StaticContentEntry(
            contentType, encoding, isBinary, entry.Hash, content, entry.LastModified);

        _built.TryAdd(entry.RoutePath, built);

        return built;
    }

    /// <summary>
    /// Reads an entry the build chose not to embed.
    /// </summary>
    /// <remarks>
    /// The path comes from the manifest rather than from the request, so there is nothing here a
    /// request can steer. A file missing at run time is a deployment that shipped the assembly
    /// without its content, which is worth a log line: it answers 404 either way, and a 404 for a
    /// file the build definitely saw is otherwise indistinguishable from a URL nobody declared.
    /// </remarks>
    private async ValueTask<byte[]?> ReadFromDisk(StaticContentManifestEntry entry) {
        var path = Path.Combine(_rootPath, entry.RelativePath!);

        try {
            return await File.ReadAllBytesAsync(path);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException) {
            _logger.LogWarning(
                "Static content {RoutePath} is in the manifest but {FilePath} is not on disk. " +
                "The application was deployed without the content the build was given",
                entry.RoutePath, path);

            return null;
        }
    }
}
