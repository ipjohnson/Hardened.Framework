using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Caching;

/// <summary>
/// A response held in a store, as the bytes that were written and what was written with them.
/// </summary>
/// <remarks>
/// <para>
/// Bytes rather than the value the handler returned. The point of the cache is to skip the
/// serialize as well as the handler, and a stored model would have to be serialized again on every
/// hit - which is also where the content negotiation the first request settled would have to be
/// repeated.
/// </para>
/// <para>
/// <b>The headers are what this representation is, not what its first request was.</b> Three kinds
/// are absent, all of them dropped when a response is captured rather than when one is replayed, so
/// a store written by an older build cannot leak one either: <c>Set-Cookie</c>, which belongs to a
/// caller; the hop-by-hop and transport-framing headers, which belong to a connection; and anything
/// the response already carried before the cache's own chain was entered, which the filter that
/// wrote it writes again on a hit. What is left is what the handler and the filters inside the
/// cache produced, which is what carries <c>Cache-Control</c> and <c>ETag</c> onto a hit.
/// </para>
/// </remarks>
public sealed class CachedResponse {

    public CachedResponse(
        int status,
        string? contentType,
        byte[] body,
        IReadOnlyList<KeyValuePair<string, StringValues>> headers) {
        Status = status;
        ContentType = contentType;
        Body = body;
        Headers = headers;
    }

    public int Status { get; }

    public string? ContentType { get; }

    public byte[] Body { get; }

    public IReadOnlyList<KeyValuePair<string, StringValues>> Headers { get; }

    /// <summary>
    /// What this entry costs a store that caps its size.
    /// </summary>
    /// <remarks>
    /// The body alone. Header names and values are bounded and small next to it, and a size that
    /// walks them would be paid on every store for a correction below the noise.
    /// </remarks>
    public long Size => Body.Length;
}
