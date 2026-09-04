using System.IO.Compression;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Runtime.Compression;

/// <inheritdoc cref="ICompressionConfiguration"/>
public class CompressionConfiguration : ICompressionConfiguration {

    /// <summary>
    /// Kestrel's request body limit, which is the number ASP.NET's own decompression middleware
    /// enforces against the decoded size.
    /// </summary>
    public const long DefaultMaxDecompressedRequestBytes = 30_000_000;

    /// <summary>
    /// gzip first. Every client accepts it, including a browser on plain HTTP, which advertises
    /// Brotli only over TLS. Brotli's output is smaller at the same level, so an operation that
    /// knows its clients can favour it.
    /// </summary>
    public List<string> Encodings { get; set; } = [KnownEncoding.GZip, KnownEncoding.Br];

    /// <summary>
    /// <see cref="CompressionLevel.Fastest"/>, as ASP.NET Core chose. At a typical body the
    /// difference between levels is tens of microseconds of CPU for a few hundred bytes.
    /// </summary>
    public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;

    /// <summary>
    /// JSON, problem JSON and any <c>+json</c>; XML and any <c>+xml</c>; JavaScript; NDJSON; SVG;
    /// and <c>text/*</c>, from which <see cref="ExcludedMediaTypes"/> removes event streams.
    /// </summary>
    public List<string> MediaTypes { get; set; } = [
        KnownContentType.Json,
        "application/problem+json",
        "application/*+json",
        "application/xml",
        "application/*+xml",
        "application/javascript",
        KnownContentType.NdJson,
        "image/svg+xml",
        "text/*"
    ];

    /// <summary>
    /// Event streams. A sync flush per event keeps the stream live through an encoder, so this is
    /// convention rather than necessity: an <c>EventSource</c> reconnects on its own and a proxy
    /// in front of it is the more usual place to compress.
    /// </summary>
    public List<string> ExcludedMediaTypes { get; set; } = [KnownContentType.EventStream];

    public long MaxDecompressedRequestBytes { get; set; } = DefaultMaxDecompressedRequestBytes;

    IReadOnlyList<string> ICompressionConfiguration.Encodings => Encodings;

    IReadOnlyList<string> ICompressionConfiguration.MediaTypes => MediaTypes;

    IReadOnlyList<string> ICompressionConfiguration.ExcludedMediaTypes => ExcludedMediaTypes;

    public bool Compresses(string? contentType) {
        if (string.IsNullOrEmpty(contentType)) {
            return false;
        }

        var mediaType = contentType.AsSpan();
        var parameters = mediaType.IndexOf(';');

        if (parameters >= 0) {
            mediaType = mediaType.Slice(0, parameters);
        }

        mediaType = mediaType.Trim();

        foreach (var excluded in ExcludedMediaTypes) {
            if (mediaType.Equals(excluded, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        foreach (var pattern in MediaTypes) {
            if (Matches(pattern, mediaType)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="mediaType"/> falls under <paramref name="pattern"/>.
    /// </summary>
    /// <remarks>
    /// Written here rather than through <see cref="MediaType.Matches"/>, which answers a different
    /// question - whether a client's <c>Accept</c> admits what a serializer produces - and knows
    /// nothing of structured suffixes. <c>application/*+json</c> is the whole reason for a rule
    /// rather than a list: it is what makes <c>application/vnd.api+json</c> compress without anyone
    /// having listed it.
    /// </remarks>
    private static bool Matches(string pattern, ReadOnlySpan<char> mediaType) {
        var patternSlash = pattern.IndexOf('/');
        var mediaSlash = mediaType.IndexOf('/');

        if (patternSlash < 0 || mediaSlash < 0) {
            return false;
        }

        var patternType = pattern.AsSpan(0, patternSlash);
        var patternSubtype = pattern.AsSpan(patternSlash + 1);
        var type = mediaType.Slice(0, mediaSlash);
        var subtype = mediaType.Slice(mediaSlash + 1);

        if (!patternType.Equals("*", StringComparison.Ordinal) &&
            !patternType.Equals(type, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (patternSubtype.Equals("*", StringComparison.Ordinal)) {
            return true;
        }

        if (patternSubtype.StartsWith("*+", StringComparison.Ordinal)) {
            return subtype.EndsWith(patternSubtype.Slice(1), StringComparison.OrdinalIgnoreCase);
        }

        return patternSubtype.Equals(subtype, StringComparison.OrdinalIgnoreCase);
    }
}
