using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// Whether a GET or HEAD can be answered 304, from what the caller says it holds and the validators
/// the response would have carried.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is RFC 9110 §13.2.1's, and it is the whole of the rule.</b> <c>If-None-Match</c> is
/// evaluated when it is present, and <c>If-Modified-Since</c> only when it is not - including when
/// the tag does <em>not</em> match, in which case the date is not consulted at all. A validator is a
/// stronger statement than a timestamp, and a client that sent both meant the validator.
/// </para>
/// <para>
/// One implementation, asked by the conditional-request filter about a handler's response and by
/// the static content writer about a file's, so the two cannot disagree about the rule.
/// </para>
/// <para>
/// A response carrying no validator is never a 304. A client can only revalidate what it was given
/// a validator for, so nothing here computes one: the response cache tags what it stores, static
/// content hashes what it serves, and a handler that knows its resource's version writes
/// <c>ETag</c> or <c>Last-Modified</c> itself.
/// </para>
/// </remarks>
public static class ConditionalGet {

    /// <summary>
    /// Whether the caller already holds the representation described by <paramref name="etag"/>
    /// and <paramref name="lastModified"/>.
    /// </summary>
    /// <param name="ifNoneMatch">The request's <c>If-None-Match</c>, or empty.</param>
    /// <param name="ifModifiedSince">The request's <c>If-Modified-Since</c>, or empty.</param>
    /// <param name="etag">The response's entity-tag, quotes included, or null when it has none.</param>
    /// <param name="lastModified">
    /// When the representation last changed, or null when nothing knows. Compared to the second,
    /// which is the precision the header has.
    /// </param>
    public static bool NotModified(
        StringValues ifNoneMatch,
        StringValues ifModifiedSince,
        string? etag,
        DateTimeOffset? lastModified) {
        if (ifNoneMatch.Count > 0) {
            return etag != null && EntityTagHeader.Matches(ifNoneMatch, etag);
        }

        if (lastModified == null) {
            return false;
        }

        // Not newer than what the client was given. Equality counts as unchanged: the header has
        // one-second precision, so "the same second" is as close to "the same" as it can express.
        return HttpDate.TryParse(ifModifiedSince, out var since) &&
               HttpDate.Truncate(lastModified.Value) <= since;
    }
}
