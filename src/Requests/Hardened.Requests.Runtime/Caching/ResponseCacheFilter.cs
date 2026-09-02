using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// Answers from the store when it can, and fills the store when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// Installed at <see cref="Abstract.RequestFilter.FilterOrder.ResponseCache"/>, one stage ahead of
/// <c>IoFilter</c>. That is what makes a hit skip the bind, the handler and the serialize rather
/// than only the handler - the whole point of storing bytes instead of the value the handler
/// returned.
/// </para>
/// <para>
/// A hit returns without calling <c>Next</c>, which is the pipeline's only mechanism for refusing to
/// go further. Filters ordered ahead of this one still complete their own work on the way back out,
/// so a request served from the store is still measured and still logged.
/// </para>
/// <para>
/// <b>Not for a handler that streams.</b> Capturing a response means buffering it, so a handler
/// returning <c>IAsyncEnumerable&lt;T&gt;</c> would hold its whole sequence in memory and answer no
/// sooner than it finished. This does not refuse one: whether a handler streams is decided by the
/// generator choosing <c>AsyncEnumerableIoFilter</c>, and it is not on
/// <see cref="IExecutionRequestHandlerInfo"/>, so there is nothing here to read the way
/// <c>[CacheResponse]</c> reads the requirement.
/// </para>
/// </remarks>
public sealed class ResponseCacheFilter : IExecutionFilter {

    /// <summary>
    /// What an attribute that names no <c>Duration</c> gets: 60 seconds.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core's output cache defaults to the same minute. The alternative default is
    /// "forever", which is the wrong one for a store nothing can invalidate by tag yet.
    /// </remarks>
    public const int DefaultDuration = 60;

    /// <summary>
    /// What separates the parts of a composite key.
    /// </summary>
    /// <remarks>
    /// The unit separator. It cannot appear in a header value, a query value or a route token, so
    /// no two different requests can compose the same key by moving the boundary between two parts.
    /// </remarks>
    private const char Separator = '\u001f';

    private readonly ICacheKeyProvider[] _keyProviders;
    private readonly string _handlerKey;
    private readonly TimeSpan _duration;

    /// <summary>
    /// Resolved once, on the first request this filter serves. There is no service provider where
    /// the filter is built - <c>IRequestFilterProvider.GetFilters</c> is handed the handler and
    /// nothing else - and a per-request lookup for a singleton would be paid on every hit.
    /// </summary>
    private IResponseCacheStore? _store;

    public ResponseCacheFilter(ICacheKeyProvider[] keyProviders, string handlerKey, int duration) {
        _keyProviders = keyProviders;
        _handlerKey = handlerKey;
        _duration = TimeSpan.FromSeconds(duration <= 0 ? DefaultDuration : duration);
    }

    /// <summary>
    /// One filter over everything <paramref name="declarations"/> asked for.
    /// </summary>
    /// <remarks>
    /// Called once per handler, as its filter chain is built. Every failure a declaration can
    /// express - a strategy handed values it cannot use, two durations that disagree - is raised
    /// here, naming the handler, rather than on a request.
    /// </remarks>
    public static ResponseCacheFilter Compose(
        IExecutionRequestHandlerInfo handlerInfo,
        IReadOnlyList<ICacheResponseDeclaration> declarations) {
        var providers = new ICacheKeyProvider[declarations.Count];
        var duration = 0;

        for (var i = 0; i < declarations.Count; i++) {
            var declaration = declarations[i];

            try {
                providers[i] = declaration.CreateKeyProvider();
            }
            catch (Exception exception) {
                throw new InvalidOperationException(
                    $"[CacheResponse] on {handlerInfo.Method} {handlerInfo.Path} could not build its " +
                    $"cache key strategy: {exception.Message}",
                    exception);
            }

            if (declaration.Duration == 0) {
                continue;
            }

            if (duration != 0 && duration != declaration.Duration) {
                throw new InvalidOperationException(
                    $"{handlerInfo.Method} {handlerInfo.Path} declares [CacheResponse] twice with " +
                    $"different durations, {duration} and {declaration.Duration}. Composed " +
                    "attributes share one lifetime, so set Duration on one of them.");
            }

            // First one wins. The loop still runs to the end, so a later disagreement is found
            // rather than shadowed by the winner.
            if (duration == 0) {
                duration = declaration.Duration;
            }
        }

        return new ResponseCacheFilter(
            providers, handlerInfo.Method + " " + handlerInfo.Path, duration);
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var key = await Key(context);

        if (key == null) {
            await chain.Next();

            return;
        }

        var store = Store(context);
        var cached = await store.Get(key, context.CancellationToken);

        if (cached != null) {
            await Replay(context, cached);

            return;
        }

        await CaptureAndStore(chain, store, key);
    }

    /// <summary>
    /// The composite key, or null when any strategy declined this request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefixed with the handler's method and path, so two handlers keyed the same way - two routes
    /// both varying on <c>culture</c> - do not answer each other's requests.
    /// </para>
    /// <para>
    /// A null from any one strategy leaves the whole request neither looked up nor stored. There is
    /// no partial key: a response varying on something a strategy could not read is a response that
    /// must not be shared.
    /// </para>
    /// </remarks>
    private async ValueTask<string?> Key(IExecutionContext context) {
        // One strategy is the ordinary case, and needs neither a builder nor a separator.
        if (_keyProviders.Length == 1) {
            var only = await _keyProviders[0].Key(context);

            return only == null ? null : _handlerKey + Separator + only;
        }

        var key = new StringBuilder(_handlerKey);

        foreach (var provider in _keyProviders) {
            var part = await provider.Key(context);

            if (part == null) {
                return null;
            }

            key.Append(Separator).Append(part);
        }

        return key.ToString();
    }

    private IResponseCacheStore Store(IExecutionContext context) {
        // Racy by construction and harmless: two requests may both resolve, and both are handed the
        // same singleton.
        return _store ??=
            context.RootServiceProvider.GetService<IResponseCacheStore>() ??
            throw new ResponseCacheStoreMissingException(_handlerKey);
    }

    /// <summary>
    /// Writes a stored response as though the handler had just produced it.
    /// </summary>
    private static async Task Replay(IExecutionContext context, CachedResponse cached) {
        var response = context.Response;

        response.Status = cached.Status;

        foreach (var header in cached.Headers) {
            response.Headers[header.Key] = header.Value;
        }

        // After the headers, because on a header-backed response ContentType is one of them, and
        // the stored content type is the one that was actually sent. Only when there is one:
        // assigning null writes an empty Content-Type on a response backed by a plain dictionary,
        // which is not what "the handler set none" looked like the first time.
        if (cached.ContentType != null) {
            response.ContentType = cached.ContentType;
        }

        // Nothing downstream runs, so nothing else will write this response. The flag is what keeps
        // ResponseFinalizerFilter from writing a second one on the way out.
        response.ShouldSerialize = false;

        await response.Body.WriteAsync(cached.Body, context.CancellationToken);
    }

    /// <summary>
    /// Runs the chain with the response buffered, then writes the buffer out and keeps a copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer is what makes the bytes available at all. They are written to a stream the
    /// transport owns, and on Kestrel that stream cannot be read back; swapping it is the same
    /// thing a compressing response does to the same stream.
    /// </para>
    /// <para>
    /// <b>The real body is written in a <c>finally</c>.</b> A handler that threw still produces a
    /// response - the exception serializer writes one at <c>FilterOrder.Serialization</c>, which is
    /// inside this - so dropping the buffer on the way out would turn every failure into an empty
    /// body. Only the storing half is conditional.
    /// </para>
    /// </remarks>
    private async Task CaptureAndStore(IExecutionChain chain, IResponseCacheStore store, string key) {
        var context = chain.Context;
        var response = context.Response;
        var transportBody = response.Body;
        var buffer = new MemoryStream();

        response.Body = buffer;

        try {
            await chain.Next();
        }
        finally {
            response.Body = transportBody;

            buffer.Position = 0;

            await buffer.CopyToAsync(transportBody, context.CancellationToken);
        }

        if (!IsStorable(response)) {
            return;
        }

        var entry = new CachedResponse(
            response.Status ?? 200,
            response.ContentType,
            buffer.ToArray(),
            Replayable(response.Headers));

        await store.Set(key, entry, _duration, context.CancellationToken);
    }

    /// <summary>
    /// Whether this response is one a later request may be given.
    /// </summary>
    /// <remarks>
    /// 200 only. A 404 or a 500 is about the moment rather than the resource, and a redirect or a
    /// 304 carries its meaning in headers this does not model. Narrow on purpose: widening it is a
    /// decision, and having widened it by accident is a defect nobody sees until a transient failure
    /// is served for a minute.
    /// </remarks>
    private static bool IsStorable(IExecutionResponse response) =>
        response.ExceptionValue == null && (response.Status ?? 200) == 200;

    /// <summary>
    /// The response headers a second caller may be given.
    /// </summary>
    /// <remarks>
    /// Everything except <c>Set-Cookie</c>, which is about the caller rather than the representation
    /// - replaying one hands a second caller the first one's session. Stripped as the response is
    /// captured rather than as one is replayed, so a store written by an older build cannot leak one
    /// either.
    /// </remarks>
    private static IReadOnlyList<KeyValuePair<string, StringValues>> Replayable(
        IDictionary<string, StringValues> headers) {
        var replayable = new List<KeyValuePair<string, StringValues>>(headers.Count);

        foreach (var header in headers) {
            if (string.Equals(header.Key, KnownHeaders.SetCookie, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            replayable.Add(header);
        }

        return replayable;
    }
}
