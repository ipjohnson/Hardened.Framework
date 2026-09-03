using System.Collections.Frozen;
using System.Text;
using Hardened.Requests.Abstract.Authorization;
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
/// <b>A request a filter ahead of this one refused is neither answered from the store nor stored.</b>
/// Those filters record the refusal and continue rather than short-circuiting, because the filter
/// that writes it is behind them - so "still travelling" does not mean "still permitted", and this
/// stage has to read what they recorded rather than infer it from having been reached.
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
    /// "forever", which is the wrong one to reach by saying nothing: a declaration that meant it
    /// can name a duration, and one that did not think about it should not get an entry only a
    /// tag can ever remove.
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

    /// <summary>
    /// The response headers a stored entry never keeps, whatever wrote them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hop-by-hop first, and that is the whole of a defect.</b> A response framed by the host as
    /// <c>Transfer-Encoding: chunked</c> had that header captured with it, and a hit re-declared
    /// chunked framing and then wrote the stored bytes with no chunk header and no terminator: zero
    /// body on Kestrel, a protocol error on ASP.NET Core, on every cached operation and every key
    /// strategy. RFC 9110 is explicit that these describe a connection rather than a representation
    /// and must never be stored or forwarded, and the reason nothing here caught it is that
    /// <c>ITestWebApp</c> has no transport to set one.
    /// </para>
    /// <para>
    /// <c>Content-Length</c> for the same reason from the other end: the transport frames what is
    /// actually written on the hit, and a stored length can only duplicate or contradict it.
    /// <c>Date</c> and <c>Server</c> belong to the host and to the moment - a replayed <c>Date</c>
    /// is what every downstream cache computes an age from.
    /// </para>
    /// <para>
    /// <c>Set-Cookie</c> is here because it belongs to the caller rather than to the
    /// representation. Replaying one hands a second caller the first one's session.
    /// </para>
    /// </remarks>
    private static readonly FrozenSet<string> NotStored = new[] {
        KnownHeaders.SetCookie,
        KnownHeaders.TransferEncoding,
        KnownHeaders.ContentLength,
        KnownHeaders.Connection,
        KnownHeaders.KeepAlive,
        KnownHeaders.TE,
        KnownHeaders.Trailer,
        KnownHeaders.Upgrade,
        KnownHeaders.ProxyAuthenticate,
        KnownHeaders.ProxyAuthorization,
        KnownHeaders.Date,
        KnownHeaders.Server
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly ICacheKeyProvider[] _keyProviders;
    private readonly string _handlerKey;
    private readonly TimeSpan _duration;
    private readonly CacheScope _scope;
    private readonly string[] _tags;

    /// <summary>
    /// Resolved once, on the first request this filter serves. There is no service provider where
    /// the filter is built - <c>IRequestFilterProvider.GetFilters</c> is handed the handler and
    /// nothing else - and a per-request lookup for a singleton would be paid on every hit.
    /// </summary>
    private IResponseCacheStore? _store;

    /// <param name="scope">
    /// Who a stored response may be served to. <see cref="CacheScope.Unstated"/> is taken as
    /// <see cref="CacheScope.AllCallers"/> here: whether leaving it unstated is allowed at all is
    /// decided in <see cref="Compose"/>, which is the half that can see what the handler requires
    /// of its caller.
    /// </param>
    /// <param name="tags">
    /// The names an entry from this handler can be invalidated by, or none.
    /// </param>
    public ResponseCacheFilter(
        ICacheKeyProvider[] keyProviders,
        string handlerKey,
        int duration,
        CacheScope scope = CacheScope.AllCallers,
        string[]? tags = null) {
        _keyProviders = keyProviders;
        _handlerKey = handlerKey;
        _duration = TimeSpan.FromSeconds(duration <= 0 ? DefaultDuration : duration);
        _scope = scope;
        _tags = tags ?? [];
    }

    /// <summary>
    /// One filter over everything <paramref name="declarations"/> asked for.
    /// </summary>
    /// <remarks>
    /// Called once per handler, as its filter chain is built. Every failure a declaration can
    /// express - a strategy handed values it cannot use, two durations that disagree, a guarded
    /// handler that has not said who its stored response may be served to - is raised here, naming
    /// the handler, rather than on a request.
    /// </remarks>
    public static ResponseCacheFilter Compose(
        IExecutionRequestHandlerInfo handlerInfo,
        IReadOnlyList<ICacheResponseDeclaration> declarations) {
        var providers = new ICacheKeyProvider[declarations.Count];
        var duration = 0;
        var scope = CacheScope.Unstated;
        List<string>? tags = null;

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

            foreach (var tag in declaration.Tags) {
                // Deduped, because two composed declarations naming the same tag is one tag, and an
                // index that held the key twice would have to be right about removing it twice.
                if (!(tags ??= []).Contains(tag, StringComparer.Ordinal)) {
                    tags.Add(tag);
                }
            }

            if (declaration.Scope != CacheScope.Unstated) {
                if (scope != CacheScope.Unstated && scope != declaration.Scope) {
                    throw new InvalidOperationException(
                        $"{handlerInfo.Method} {handlerInfo.Path} declares [CacheResponse] twice " +
                        $"with different scopes, {scope} and {declaration.Scope}. Composed " +
                        "attributes share one entry, so it has one audience.");
                }

                scope = declaration.Scope;
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

        var handlerKey = handlerInfo.Method + " " + handlerInfo.Path;
        var requirement = handlerInfo.Requirement;

        if (scope == CacheScope.Unstated) {
            // A handler that requires nothing of its caller has one audience whatever it answers,
            // so there is nothing for an author to decide and nothing to interrupt them over.
            if (requirement != null) {
                throw new CacheScopeUndeclaredException(handlerKey, requirement);
            }

            scope = CacheScope.AllCallers;
        }

        return new ResponseCacheFilter(providers, handlerKey, duration, scope, tags?.ToArray());
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // A request already refused is not one the store may answer.
        //
        // Everything ahead of FilterOrder.Serialization refuses by recording the failure on the
        // response and calling Next, so that the serialization filter can write it - which means an
        // authorization or rate-limit refusal reaches this filter as a request that is still
        // travelling. Replaying a stored 200 over it discards the refusal and answers the caller who
        // was turned away, from an entry a permitted caller filled. Both refusers sit ahead of this
        // stage precisely so they settle first; reading what they recorded is what makes that
        // ordering mean anything.
        if (context.Response.ExceptionValue != null) {
            await chain.Next();

            return;
        }

        var store = Store(context);

        if (store == null) {
            // Recorded and continued rather than thrown, which is the rule for everything ahead of
            // FilterOrder.Serialization and one this filter used to break: throwing here unwound
            // past the filter that writes a response, so the message naming the handler reached the
            // log and the caller got a 500 with Content-Length: 0.
            context.Response.ExceptionValue = new ResponseCacheStoreMissingException(_handlerKey);

            await chain.Next();

            return;
        }

        var key = await Key(context);

        if (key == null) {
            await chain.Next();

            return;
        }

        var cached = await store.Get(key, context.CancellationToken);

        if (cached != null) {
            await Replay(context, cached);

            return;
        }

        await CaptureAndStore(chain, store, key);
    }

    /// <summary>
    /// The composite key, or null when a strategy declined this request or the caller cannot be
    /// told apart from another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefixed with the handler's method and path, so two handlers keyed the same way - two routes
    /// both varying on <c>culture</c> - do not answer each other's requests, and then by the caller
    /// when the scope is <see cref="CacheScope.PerCaller"/>.
    /// </para>
    /// <para>
    /// A null from any one strategy leaves the whole request neither looked up nor stored. There is
    /// no partial key: a response varying on something a strategy could not read is a response that
    /// must not be shared.
    /// </para>
    /// </remarks>
    private async ValueTask<string?> Key(IExecutionContext context) {
        string? caller = null;

        if (_scope == CacheScope.PerCaller) {
            caller = Caller(context.CallerPrincipal);

            if (caller == null) {
                return null;
            }
        }

        // One strategy and one audience is the ordinary case, and needs neither a builder nor a
        // separator.
        if (_keyProviders.Length == 1 && caller == null) {
            var only = await _keyProviders[0].Key(context);

            return only == null ? null : _handlerKey + Separator + only;
        }

        var key = new StringBuilder(_handlerKey);

        if (caller != null) {
            key.Append(Separator).Append(caller);
        }

        foreach (var provider in _keyProviders) {
            var part = await provider.Key(context);

            if (part == null) {
                return null;
            }

            key.Append(Separator).Append(part);
        }

        return key.ToString();
    }

    /// <summary>
    /// What separates one caller's entries from another's, or null when nothing does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The issuer as well as the subject. A subject is unique to whoever issued it, so two trusted
    /// issuers naming the same subject are two callers - and an application that accepts more than
    /// one is exactly the application where that matters.
    /// </para>
    /// <para>
    /// A caller with no subject returns null, which leaves the request neither looked up nor
    /// stored. The alternative is one entry shared by every caller who has no subject, which is the
    /// entry <see cref="CacheScope.PerCaller"/> exists to refuse.
    /// </para>
    /// </remarks>
    private static string? Caller(ICallerPrincipal principal) {
        var subject = principal.Subject;

        return string.IsNullOrEmpty(subject) ? null : principal.Issuer + Separator + subject;
    }

    /// <summary>
    /// The registered store, or null when the application registered none.
    /// </summary>
    /// <remarks>
    /// Asked before the key is composed, so a declaration with nowhere to put its answers fails the
    /// same way on every request rather than only on the requests a strategy was willing to key -
    /// and so a <c>ByPayload</c> handler does not hash a body on the way to failing.
    /// </remarks>
    private IResponseCacheStore? Store(IExecutionContext context) {
        // Racy by construction and harmless: two requests may both resolve, and both are handed the
        // same singleton.
        return _store ??= context.RootServiceProvider.GetService<IResponseCacheStore>();
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

        // Taken before the chain is entered, and taken after the key was composed so that a
        // strategy writing its own header - VaryByHeader writes Vary - is on this side of the line
        // rather than captured. See Replayable.
        var carried = Carried(response.Headers);

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
            Replayable(response.Headers, carried),
            _tags);

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
    /// Headers this response already carried before the chain inside the cache was entered.
    /// </summary>
    /// <remarks>
    /// Allocated on the miss path only, next to a buffer holding the whole body.
    /// </remarks>
    private static Dictionary<string, StringValues> Carried(
        IDictionary<string, StringValues> headers) {
        var carried = new Dictionary<string, StringValues>(
            headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers) {
            carried[header.Key] = header.Value;
        }

        return carried;
    }

    /// <summary>
    /// The response headers a second caller may be given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stripped as the response is captured rather than as one is replayed, so a store written by
    /// an older build cannot leak one either.
    /// </para>
    /// <para>
    /// <b>Only what the chain inside the cache produced.</b> A header this response already carried
    /// on the way in was written by a filter ordered ahead of this stage, which runs on a hit as
    /// well as on a miss - so storing its value freezes one request's <c>X-Correlation-Id</c>, or
    /// one request's <c>RateLimit-Remaining</c>, onto every later caller, while leaving out
    /// nothing: the filter that wrote it writes it again. A header the chain <em>changed</em> is
    /// kept, because a miss would have changed it the same way.
    /// </para>
    /// <para>
    /// <see cref="NotStored"/> covers what is never the representation's whatever wrote it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<KeyValuePair<string, StringValues>> Replayable(
        IDictionary<string, StringValues> headers,
        Dictionary<string, StringValues> carried) {
        var replayable = new List<KeyValuePair<string, StringValues>>(headers.Count);

        foreach (var header in headers) {
            if (NotStored.Contains(header.Key)) {
                continue;
            }

            if (carried.TryGetValue(header.Key, out var before) && before.Equals(header.Value)) {
                continue;
            }

            replayable.Add(header);
        }

        return replayable;
    }
}
