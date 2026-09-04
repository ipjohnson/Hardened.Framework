using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// An entity-tag, and whether an <c>If-None-Match</c> names it.
/// </summary>
/// <remarks>
/// <para>
/// <c>If-None-Match</c> is a list, not a value: <c>*</c>, or one or more entity-tags separated by
/// commas, each optionally weak. Comparing the header against a tag with string equality answers
/// correctly for exactly one of those forms - a lone strong tag - and serves a full body for the
/// other three.
/// </para>
/// <para>
/// <b>Weak comparison, per RFC 9110 §13.1.2</b>, which requires it for <c>If-None-Match</c>
/// specifically: a client that was given <c>W/"x"</c> and one that was given <c>"x"</c> are both
/// asking about the same representation. Strong comparison belongs to <c>If-Match</c> and
/// <c>If-Range</c>, and neither is implemented here yet.
/// </para>
/// <para>
/// The list is walked rather than split on commas. An entity-tag's opaque part may itself contain a
/// comma - <c>etagc</c> admits <c>%x21 / %x23-7E</c> - so splitting first can cut a tag in half. Ours
/// are base64 and never would, but a validator this receives came from whatever served the resource
/// last, which on a re-deploy behind a CDN need not be this process.
/// </para>
/// </remarks>
public static class EntityTagHeader {

    private const string WeakPrefix = "W/";

    /// <summary>
    /// Formats <paramref name="opaque"/> as an entity-tag.
    /// </summary>
    /// <remarks>
    /// The quotes are not decoration. <c>entity-tag = [ weak ] opaque-tag</c> and
    /// <c>opaque-tag = DQUOTE *etagc DQUOTE</c>, so a bare token is not one - and base64, which is
    /// what the hash arrives as, contains <c>+</c>, <c>/</c> and <c>=</c>, none of which may appear
    /// unquoted in a header value of this shape.
    /// </remarks>
    public static string Format(string opaque) => "\"" + opaque + "\"";

    /// <summary>
    /// Formats <paramref name="opaque"/> as the entity-tag of a <paramref name="variant"/> of the
    /// same resource - the gzip-encoded form of a document, say.
    /// </summary>
    /// <remarks>
    /// Two representations of one resource must not share a validator: a cache holding both, or a
    /// client switching between them, otherwise revalidates one against the other's tag and is told
    /// nothing changed. The variant is folded into the opaque part rather than carried beside it,
    /// because an entity-tag has nowhere else to put it.
    /// </remarks>
    public static string Format(string opaque, string variant) =>
        "\"" + opaque + "-" + variant + "\"";

    /// <summary>
    /// The strong entity-tag for <paramref name="content"/> exactly as it is sent: a SHA-256 of
    /// the bytes, base64, quoted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One definition of what a computed tag looks like, for the response cache tagging what it
    /// stores and <c>[ConditionalGet]</c> tagging what it sends, so the same bytes carry the same
    /// tag whichever of them computed it.
    /// </para>
    /// <para>
    /// SHA-256 rather than something cheaper, for the reasons <c>ByPayload</c> gives: two
    /// representations colliding hands a client a 304 for a body it does not hold, and
    /// <c>MD5.Create()</c> throws outright on a FIPS-enforcing host. Strong, because it names
    /// exactly these bytes; whatever re-encodes them on the way out weakens it.
    /// </para>
    /// </remarks>
    public static string ForContent(ReadOnlySpan<byte> content) =>
        Format(Convert.ToBase64String(SHA256.HashData(content)));

    /// <summary>
    /// Whether <paramref name="ifNoneMatch"/> names <paramref name="etag"/>, or is <c>*</c>.
    /// </summary>
    /// <param name="etag">
    /// The tag as it would be sent, quotes included. A weak marker on it is ignored, as it is on
    /// every candidate.
    /// </param>
    public static bool Matches(StringValues ifNoneMatch, string etag) {
        if (ifNoneMatch.Count == 0 || string.IsNullOrEmpty(etag)) {
            return false;
        }

        var expected = Opaque(etag);

        foreach (var value in ifNoneMatch) {
            if (string.IsNullOrEmpty(value)) {
                continue;
            }

            var index = 0;

            while (TryReadTag(value, ref index, out var candidate, out var wildcard)) {
                // "*" matches any current representation, which is what having one here means.
                if (wildcard) {
                    return true;
                }

                if (string.Equals(candidate, expected, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The opaque part of <paramref name="tag"/>: its weak marker and quotes removed.
    /// </summary>
    /// <remarks>
    /// A tag arriving without quotes is returned as written rather than rejected. It is malformed,
    /// but it is also what a server that made the mistake this class exists to fix would have sent,
    /// and refusing to match it would turn every conditional request from a client holding one into
    /// a full body forever.
    /// </remarks>
    private static string Opaque(string tag) {
        var span = tag.AsSpan().Trim();

        if (span.StartsWith(WeakPrefix.AsSpan(), StringComparison.Ordinal)) {
            span = span.Slice(WeakPrefix.Length);
        }

        if (span.Length >= 2 && span[0] == '"' && span[span.Length - 1] == '"') {
            span = span.Slice(1, span.Length - 2);
        }

        return span.ToString();
    }

    /// <summary>
    /// Reads the next entity-tag from <paramref name="value"/>, advancing <paramref name="index"/>.
    /// </summary>
    /// <returns>False once the list is exhausted, or once it stops parsing.</returns>
    private static bool TryReadTag(
        string value, ref int index, out string candidate, out bool wildcard) {
        candidate = string.Empty;
        wildcard = false;

        while (index < value.Length && (value[index] == ',' || char.IsWhiteSpace(value[index]))) {
            index++;
        }

        if (index >= value.Length) {
            return false;
        }

        if (value[index] == '*') {
            index++;
            wildcard = true;

            return true;
        }

        if (string.CompareOrdinal(value, index, WeakPrefix, 0, WeakPrefix.Length) == 0) {
            index += WeakPrefix.Length;
        }

        if (index >= value.Length || value[index] != '"') {
            // Not an entity-tag. Stop rather than resynchronise: the rest of a header this
            // malformed says nothing reliable about what the client holds.
            index = value.Length;

            return false;
        }

        var start = ++index;

        while (index < value.Length && value[index] != '"') {
            index++;
        }

        if (index >= value.Length) {
            // Unterminated. Same reasoning.
            return false;
        }

        candidate = value.Substring(start, index - start);
        index++;

        return true;
    }
}
