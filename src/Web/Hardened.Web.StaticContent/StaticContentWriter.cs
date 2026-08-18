using System.Globalization;
using System.IO.Compression;
using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Web.Runtime.CacheControl;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Turns an entry into a response: which representation, which headers, and how much of a body.
/// </summary>
/// <remarks>
/// One implementation, written against <see cref="StaticContentEntry"/> and nothing else, so that
/// every source shares it. Where the bytes came from - a directory, a manifest, a file re-read
/// because it changed - decides nothing about what the wire sees.
/// </remarks>
public static class StaticContentWriter {

    /// <summary>
    /// What a compressed representation's response depends on. One instance: it never varies.
    /// </summary>
    private static readonly StringValues VaryOnAcceptEncoding = new(KnownHeaders.AcceptEncoding);

    /// <summary>
    /// Answers with <paramref name="entry"/>, in whichever representation the request admits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The representation is decided first, because everything else depends on it. Which bytes go
    /// out decides which validator describes them, and a 304 has to be answered against the tag of
    /// the representation the client would otherwise have received rather than against the
    /// resource's in general.
    /// </para>
    /// <para>
    /// <b>The cache headers precede the not-modified return rather than following it.</b> RFC 9110
    /// §15.4.5 wants <c>Cache-Control</c>, <c>ETag</c> and <c>Vary</c> on a 304 whenever a 200 would
    /// have carried them - and without that an asset marked <c>immutable</c> is revalidated on every
    /// request after the first, the one thing marking it immutable exists to prevent.
    /// </para>
    /// <para>
    /// Order of the three conditionals is fixed by §13.2.1: <c>If-None-Match</c> outranks
    /// <c>If-Modified-Since</c>, and a <c>Range</c> is considered only once the representation has
    /// been established as the one the client already holds.
    /// </para>
    /// </remarks>
    public static Task Write(
        IExecutionContext context,
        StaticContentEntry entry,
        IStaticContentConfiguration configuration,
        IMemoryStreamPool memoryStreamPool,
        string? cacheControl) {
        var response = context.Response;
        var headers = response.Headers;

        // Nothing here is a serializable value, and leaving serialization on would have the locator
        // pick a serializer from whatever the client happened to send in Accept.
        response.ShouldSerialize = false;

        var sendEncoded =
            entry.IsEncoded &&
            AcceptEncodingHeader.Accepts(
                RequestHeader(context, KnownHeaders.AcceptEncoding), entry.ContentEncoding!);

        if (cacheControl != null) {
            headers[KnownHeaders.CacheControl] = new StringValues(cacheControl);
        }

        // Only when a compressed representation exists, and then always. A resource stored one way
        // is served the same bytes to every client and does not vary; one stored compressed is
        // served two different bodies at one URL, and a shared cache that did not know would hand a
        // client that cannot inflate them the ones that need it.
        if (entry.IsEncoded) {
            headers[KnownHeaders.Vary] = VaryOnAcceptEncoding;
        }

        if (entry.LastModifiedHeader != null) {
            headers[KnownHeaders.LastModified] = new StringValues(entry.LastModifiedHeader);
        }

        var etag = configuration.EnableETag
            ? sendEncoded ? entry.EncodedETag : entry.ETag
            : null;

        if (etag != null) {
            headers[KnownHeaders.ETag] = new StringValues(etag);
        }

        if (NotModified(context, entry, etag)) {
            response.Status = (int)HttpStatusCode.NotModified;

            configuration.OnPrepareResponse?.Invoke(context);

            return Task.CompletedTask;
        }

        // Ranges over the bytes as stored, which is the resource itself only when nothing is being
        // decoded on the way out. A byte offset into a gzip stream is not a byte offset into the
        // file the client asked for, and there is no way to say which one a Content-Range meant.
        var rangeable = !entry.IsEncoded && configuration.EnableRangeRequests;

        if (rangeable) {
            headers[KnownHeaders.AcceptRanges] = RangeHeader.AcceptsBytes;
        }

        if (rangeable && RangeApplies(context, entry, etag)) {
            var result = RangeHeader.Resolve(
                RequestHeader(context, KnownHeaders.Range), entry.Content.Length, out var range);

            if (result == RangeResult.Unsatisfiable) {
                headers[KnownHeaders.ContentRange] =
                    new StringValues(ByteRange.Unsatisfied(entry.Content.Length));

                response.Status = (int)HttpStatusCode.RequestedRangeNotSatisfiable;

                configuration.OnPrepareResponse?.Invoke(context);

                return Task.CompletedTask;
            }

            if (result == RangeResult.Satisfiable) {
                response.Status = (int)HttpStatusCode.PartialContent;
                response.ContentType = entry.ContentType;

                headers[KnownHeaders.ContentRange] =
                    new StringValues(range.ContentRange(entry.Content.Length));

                configuration.OnPrepareResponse?.Invoke(context);

                return WritePartial(context, entry, range);
            }
        }

        response.Status = (int)HttpStatusCode.OK;
        response.ContentType = entry.ContentType;

        configuration.OnPrepareResponse?.Invoke(context);

        if (sendEncoded) {
            return WriteStored(context, entry);
        }

        return entry.IsEncoded
            ? WriteInflated(context, entry, memoryStreamPool)
            : WriteAsRead(context, entry);
    }

    /// <summary>
    /// Whether the client already holds this representation.
    /// </summary>
    /// <remarks>
    /// <c>If-None-Match</c> wins outright when it is present, per RFC 9110 §13.2.1 - including when
    /// it does <em>not</em> match, in which case the date is not consulted at all. A validator is a
    /// stronger statement than a timestamp, and a client that sent both meant the validator.
    /// </remarks>
    private static bool NotModified(
        IExecutionContext context, StaticContentEntry entry, string? etag) {
        var ifNoneMatch = RequestHeader(context, KnownHeaders.IfNoneMatch);

        if (ifNoneMatch.Count > 0) {
            return etag != null && EntityTagHeader.Matches(ifNoneMatch, etag);
        }

        if (entry.LastModified == null) {
            return false;
        }

        var ifModifiedSince = RequestHeader(context, KnownHeaders.IfModifiedSince);

        // Not newer than what the client was given. Equality counts as unchanged: the header has
        // one-second precision, so "the same second" is as close to "the same" as it can express.
        return TryParseDate(ifModifiedSince, out var since) && entry.LastModified <= since;
    }

    /// <summary>
    /// Whether a <c>Range</c> is to be honoured, given what <c>If-Range</c> says.
    /// </summary>
    /// <remarks>
    /// <c>If-Range</c> is the guard against resuming a download into a file that changed underneath
    /// it: the client names what it held, and a range is honoured only if that is still what is
    /// here. It does not match, the whole entity is sent - not an error, because the client wants
    /// the resource either way and has just learned its copy is stale.
    /// </remarks>
    private static bool RangeApplies(
        IExecutionContext context, StaticContentEntry entry, string? etag) {
        if (RequestHeader(context, KnownHeaders.Range).Count == 0) {
            return false;
        }

        var ifRange = RequestHeader(context, KnownHeaders.IfRange);

        if (ifRange.Count == 0) {
            return true;
        }

        var value = ifRange.ToString();

        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        // An entity-tag or a date, told apart by the shape rather than by trying both: a validator
        // is quoted or weak, and nothing else is.
        if (value.StartsWith("\"", StringComparison.Ordinal) ||
            value.StartsWith("W/", StringComparison.Ordinal)) {
            // Strong comparison here, unlike If-None-Match. A weak validator says two
            // representations are equivalent, not identical, and identical is exactly what
            // splicing bytes into a half-downloaded file requires.
            return etag != null &&
                   !value.StartsWith("W/", StringComparison.Ordinal) &&
                   string.Equals(value.Trim(), etag, StringComparison.Ordinal);
        }

        return entry.LastModified != null &&
               TryParseDate(ifRange, out var asOf) &&
               entry.LastModified == asOf;
    }

    private static bool TryParseDate(StringValues value, out DateTimeOffset parsed) {
        parsed = default;

        if (value.Count == 0) {
            return false;
        }

        // Round-trip through the one format HTTP writes. A client echoing a Last-Modified sends
        // exactly what it was given, and a malformed date is ignored rather than refused.
        return DateTimeOffset.TryParseExact(
                   value.ToString(), "R", CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed) ||
               DateTimeOffset.TryParse(
                   value.ToString(), CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);
    }

    private static async Task WriteAsRead(IExecutionContext context, StaticContentEntry entry) {
        context.Response.IsBinary = entry.IsBinary;
        context.Response.Headers[KnownHeaders.ContentLength] = entry.ContentLength;

        await context.Response.Body.WriteAsync(entry.Content, 0, entry.Content.Length);
    }

    /// <summary>The requested slice, and the length of the slice rather than of the resource.</summary>
    private static async Task WritePartial(
        IExecutionContext context, StaticContentEntry entry, ByteRange range) {
        context.Response.IsBinary = entry.IsBinary;
        context.Response.Headers[KnownHeaders.ContentLength] =
            range.Length.ToString(CultureInfo.InvariantCulture);

        await context.Response.Body.WriteAsync(entry.Content, (int)range.From, (int)range.Length);
    }

    /// <summary>
    /// Writes the stored bytes untouched, to a client that said it takes them that way.
    /// </summary>
    /// <remarks>
    /// The coding named is the one actually stored. It used to be <c>gzip</c> whatever the entry
    /// held, so a client offering <c>br</c> was handed Brotli bytes under a gzip label.
    /// </remarks>
    private static async Task WriteStored(IExecutionContext context, StaticContentEntry entry) {
        context.Response.IsBinary = true;
        context.Response.Headers[KnownHeaders.ContentEncoding] = entry.ContentEncodingHeader;
        context.Response.Headers[KnownHeaders.ContentLength] = entry.ContentLength;

        await context.Response.Body.WriteAsync(entry.Content, 0, entry.Content.Length);
    }

    /// <summary>
    /// Inflates a stored representation for a client that did not offer its coding.
    /// </summary>
    /// <remarks>
    /// No length, because nobody knows it without inflating twice. The response is chunked, which is
    /// what this branch has always produced.
    /// </remarks>
    private static async Task WriteInflated(
        IExecutionContext context, StaticContentEntry entry, IMemoryStreamPool memoryStreamPool) {
        using var memoryStream = memoryStreamPool.Get();

        await memoryStream.Item.WriteAsync(entry.Content, 0, entry.Content.Length);

        memoryStream.Item.Position = 0;

        Stream outputStream = entry.ContentEncoding switch {
            KnownEncoding.GZip => new GZipStream(memoryStream.Item, CompressionMode.Decompress, true),
            KnownEncoding.Br => new BrotliStream(memoryStream.Item, CompressionMode.Decompress, true),
            _ => throw new InvalidOperationException(
                "A coding was stored that nothing here knows how to inflate: " + entry.ContentEncoding)
        };

        context.Response.IsBinary = entry.IsBinary;

        await outputStream.CopyToAsync(context.Response.Body);
        await outputStream.DisposeAsync();
    }

    private static StringValues RequestHeader(IExecutionContext context, string name) =>
        context.Request.Headers.TryGetValue(name, out var value) ? value : StringValues.Empty;

    /// <summary>
    /// The <c>Cache-Control</c> a mount sends, or null when it sends none. Built once per mount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendered by <see cref="CacheControlHeader"/>, which is the retrofit its own remarks describe:
    /// this used to build the header inline and ignore <c>CacheControlType</c> entirely, so the
    /// static path could not express <c>no-store</c>, <c>no-cache</c>, <c>public</c>,
    /// <c>private</c> or <c>no-transform</c> at all - while the configuration carried a property
    /// that said it could.
    /// </para>
    /// <para>
    /// Null when no directive is set, so a mount that configures nothing sends no header rather
    /// than an empty one.
    /// </para>
    /// </remarks>
    public static string? CacheControlFor(IStaticContentConfiguration configuration) {
        // No max age means the mount says nothing about caching at all, which is the contract that
        // shipped and the one worth keeping: rendering the rest of the directives would put
        // "public" on a response whose author configured no caching, and "public" with no freshness
        // is a shared cache storing it under heuristics nobody chose.
        //
        // The cost is that no-store alone needs a max age set beside it. That is a smaller gap than
        // the one this closes, which was every directive but max-age being unreachable.
        if (!configuration.CacheMaxAge.HasValue) {
            return null;
        }

        return CacheControlHeader.Format(
            configuration.CacheControlType, configuration.CacheMaxAge.Value, configuration.Immutable);
    }
}
