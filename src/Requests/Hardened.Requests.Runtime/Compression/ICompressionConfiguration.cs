using System.IO.Compression;

namespace Hardened.Requests.Runtime.Compression;

/// <summary>
/// How responses are compressed and how far a compressed request is allowed to grow.
/// </summary>
/// <remarks>
/// <para>
/// In <c>Hardened.Requests.Runtime</c> rather than beside the response filter, because the
/// request-side filter reads <see cref="MaxDecompressedRequestBytes"/> from it and that filter
/// runs on every host, including the ones that never negotiate a response coding.
/// </para>
/// <para>
/// Registered by the request module with its defaults, so an application that never enables
/// response compression still has a cap on request decompression. Amend it with
/// <c>services.ConfigureCompression</c>.
/// </para>
/// </remarks>
public interface ICompressionConfiguration {
    /// <summary>
    /// The content codings the server offers, in preference order.
    /// </summary>
    /// <remarks>
    /// The first coding the client accepts is used. An operation's <c>Favor</c> moves one of these
    /// to the front for that operation; it cannot add one that is not listed here.
    /// </remarks>
    IReadOnlyList<string> Encodings { get; }

    /// <summary>
    /// The level handed to both encoders.
    /// </summary>
    CompressionLevel Level { get; }

    /// <summary>
    /// The media types the default rule compresses, as patterns.
    /// </summary>
    /// <remarks>
    /// A pattern is an exact media type, <c>type/*</c>, or <c>type/*+suffix</c>. Parameters on the
    /// response's content type are ignored. A predicate on an operation replaces this rule for
    /// that operation.
    /// </remarks>
    IReadOnlyList<string> MediaTypes { get; }

    /// <summary>
    /// Media types the default rule leaves alone even where a pattern would admit them.
    /// </summary>
    IReadOnlyList<string> ExcludedMediaTypes { get; }

    /// <summary>
    /// The most a compressed request body may decode to before the request is refused with a 413.
    /// </summary>
    long MaxDecompressedRequestBytes { get; }

    /// <summary>
    /// Whether the default rule compresses a response of <paramref name="contentType"/>.
    /// </summary>
    bool Compresses(string? contentType);
}
