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
public sealed class CachedResponse
{
    public CachedResponse(
        int status,
        string? contentType,
        byte[] body,
        IReadOnlyList<KeyValuePair<string, StringValues>> headers,
        IReadOnlyList<string>? tags = null
    )
    {
        Status = status;
        ContentType = contentType;
        Body = body;
        Headers = headers;
        Tags = tags ?? [];
        Size = body.Length + Characters(contentType, headers, Tags) * sizeof(char);
    }

    public int Status { get; }

    public string? ContentType { get; }

    public byte[] Body { get; }

    public IReadOnlyList<KeyValuePair<string, StringValues>> Headers { get; }

    /// <summary>
    /// The names this entry can be invalidated by. Empty when the declaration named none.
    /// </summary>
    /// <remarks>
    /// Carried on the entry rather than passed beside it, because a store that persists entries has
    /// to persist these with them: an index rebuilt from anything else is an index that can be
    /// wrong about which entries a tag names. See <see cref="IResponseCacheStore.EvictByTag"/>.
    /// </remarks>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// What this entry costs a store that caps its size, in bytes: the body, and the content type,
    /// headers and tags as the UTF-16 strings they are in memory.
    /// </summary>
    /// <remarks>
    /// The strings count because a body can be two bytes. A store that counted only bodies against
    /// its limit would hold any number of those. Counted once, here, rather than on every store.
    /// </remarks>
    public long Size { get; }

    private static long Characters(
        string? contentType,
        IReadOnlyList<KeyValuePair<string, StringValues>> headers,
        IReadOnlyList<string> tags
    )
    {
        long characters = contentType?.Length ?? 0;

        foreach (var header in headers)
        {
            characters += header.Key.Length;

            foreach (var value in header.Value)
            {
                characters += value?.Length ?? 0;
            }
        }

        foreach (var tag in tags)
        {
            characters += tag.Length;
        }

        return characters;
    }
}
