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
/// <b><c>Set-Cookie</c> is not among the headers.</b> It is stripped when a response is captured,
/// not when one is replayed, so a cookie cannot reach a second caller even from a store written by
/// an older build. Everything else is replayed as it was sent, which is what carries
/// <c>Cache-Control</c>, <c>ETag</c> and <c>Vary</c> onto a hit - the filters that wrote them sit
/// at the same position in the chain as the cache, and a hit returns before they run.
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
