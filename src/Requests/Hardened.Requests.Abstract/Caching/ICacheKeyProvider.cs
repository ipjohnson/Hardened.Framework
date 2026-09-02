using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Caching;

/// <summary>
/// What a cached response is keyed on.
///
/// <code>
/// public sealed class VaryByTenant : ICacheKeyProvider {
///     public static ICacheKeyProvider Create(string[] values) => new VaryByTenant();
///
///     public ValueTask&lt;string?&gt; Key(IExecutionContext context) =&gt;
///         new(context.CallerPrincipal.Subject);
/// }
/// </code>
///
/// <para>
/// A type rather than a string. Every other framework names the strategy - ASP.NET Core's
/// <c>PolicyName</c>, Spring's <c>keyGenerator</c> - because their containers resolve names at run
/// time and that is what they already do for everything else. Hardened has no reflective
/// resolution, so a string here would need a registry built at startup and would fail on the first
/// request that reached the handler. The type parameter on <c>[CacheResponse&lt;T&gt;]</c> is
/// checked by the compiler instead.
/// </para>
/// </summary>
public interface ICacheKeyProvider {

    /// <summary>
    /// Builds the provider from the attribute's positional arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static abstract, so "constructible from <c>string[]</c>" is a real constraint rather than a
    /// convention. It is reached through the generic constraint on
    /// <c>CacheResponseAttribute&lt;TProvider&gt;</c>, so nothing is resolved by reflection and
    /// nothing needs a service provider.
    /// </para>
    /// <para>
    /// This is where arity is checked. <c>params string[]</c> cannot express "this strategy takes
    /// no values", so <c>[CacheResponse&lt;ByPayload&gt;("culture")]</c> compiles clean and would
    /// otherwise ignore the argument. Throwing here turns that into a failure naming the handler,
    /// raised once as its filter chain is built rather than per request.
    /// </para>
    /// </remarks>
    static abstract ICacheKeyProvider Create(string[] values);

    /// <summary>
    /// What this request's response is stored under, or null to leave it uncached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One concept rather than ASP.NET Core's <c>AllowCacheLookup</c> and <c>AllowCacheStorage</c>
    /// pair: a request with no key is neither looked up nor stored, which is what both flags
    /// together were expressing.
    /// </para>
    /// <para>
    /// Asynchronous because the request body is one of the things a key can be built from, and a
    /// body is a stream. Kestrel refuses synchronous reads by default, so a synchronous signature
    /// would make the payload strategy unimplementable on the host most likely to want it. Every
    /// provider that reads only headers, the route or the query string answers from a completed
    /// <see cref="ValueTask{TResult}"/> and allocates nothing.
    /// </para>
    /// </remarks>
    ValueTask<string?> Key(IExecutionContext context);
}
