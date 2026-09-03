using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// Serves a stored response instead of running the handler.
///
/// <code>
/// [Get("/catalog")]
/// [CacheResponse&lt;VaryByQuery&gt;("culture", "region", Duration = 60)]
/// public Catalog Browse(string culture, string region) =&gt; _catalog.For(culture, region);
/// </code>
///
/// <para>
/// <b>The type parameter carries the strategy and the arguments configure it.</b> Every other
/// framework declares a cache key with a string - ASP.NET Core's <c>PolicyName</c>, Spring's
/// <c>keyGenerator</c>, Spring's SpEL <c>key</c> - because their containers resolve names at run
/// time. Hardened has no reflective resolution, so a name here would need a registry built at
/// startup and would fail on the first request that reached the handler. A type argument is
/// checked by the compiler, and the set stays open: <see cref="ICacheKeyProvider"/> is an
/// interface anyone can implement.
/// </para>
///
/// <para>
/// <b>Nothing is stored unless a store is registered.</b> Reference
/// <c>Hardened.Requests.Caching.Memory</c> and write <c>[HardenedMemoryResponseCache]</c>, or
/// register an <see cref="IResponseCacheStore"/> of your own. Without one the handler's first
/// request fails naming the handler, rather than the attribute quietly doing nothing.
/// </para>
///
/// <para>
/// <b>A handler that requires anything of its caller has to say who its answer may be served
/// to.</b> Set <see cref="Scope"/> to <see cref="CacheScope.PerCaller"/> when the answer depends on
/// who asked, or to <see cref="CacheScope.AllCallers"/> when every caller the guard admits gets the
/// same bytes. Leaving it unstated on a guarded handler is a failure naming the handler as its
/// filter chain is built, because both readings of silence are behaviour somebody would call a
/// defect - see <see cref="CacheScope"/>. A handler requiring nothing needs none of this.
/// </para>
///
/// <para>
/// <b>A requirement that reads the request is still never cached.</b> The filter runs at
/// <see cref="FilterOrder.ResponseCache"/>, which is after authorization over grants alone and
/// before authorization that reads bound parameters - so such a requirement does not run on a hit
/// at all, and keying per caller would not make it run. The filter is not installed, decided once
/// per handler from <see cref="Requirement.RequiresContext"/>. ASP.NET Core ships the same hazard
/// as a documentation note telling you to call <c>UseOutputCache</c> after
/// <c>UseAuthorization</c>.
/// </para>
/// </summary>
/// <typeparam name="TProvider">
/// What the response is keyed on. Constructed once per handler through
/// <see cref="ICacheKeyProvider.Create"/>, so a strategy handed values it cannot use fails as the
/// filter chain is built rather than per request.
/// </typeparam>
/// <remarks>
/// <para>
/// <b><c>AllowMultiple</c> is required, and not for the reason it usually is.</b> The compiler
/// dedupes a generic attribute on the unbound generic rather than the constructed type, so
/// <c>[CacheResponse&lt;VaryByQuery&gt;]</c> and <c>[CacheResponse&lt;VaryByHeader&gt;]</c> on one
/// method are CS0579 - "Duplicate 'CacheResponse&lt;&gt;' attribute" - with
/// <c>AllowMultiple = false</c>. Allowing it is what makes two strategies on one handler
/// expressible, which <c>[OutputCache]</c> cannot do at all.
/// </para>
/// <para>
/// The cost is that the compiler no longer catches two of the same strategy, and that
/// <see cref="Duration"/> and <see cref="Scope"/> can each appear more than once. The first
/// attribute that sets one wins and two that disagree fail as the chain is built, which keeps the
/// ordinary single-attribute case free of ceremony.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class CacheResponseAttribute<TProvider> :
    Attribute, IRequestFilterProvider, ICacheResponseDeclaration
    where TProvider : ICacheKeyProvider {

    public CacheResponseAttribute(params string[] values) {
        Values = values;
    }

    /// <summary>
    /// The strategy's positional arguments - the query keys, the header names.
    /// </summary>
    public string[] Values { get; }

    /// <summary>
    /// How long a stored response stays valid, in seconds.
    /// </summary>
    /// <remarks>
    /// 0 means <see cref="ResponseCacheFilter.DefaultDuration"/>, which is the same 60 seconds
    /// ASP.NET Core's output cache defaults to. Left as the property default rather than assigned
    /// in the constructor, so "the author did not say" and "the author said 60" stay distinguishable
    /// when two attributes are combined.
    /// </remarks>
    public int Duration { get; set; }

    /// <summary>
    /// Who a stored response may be served to.
    /// </summary>
    /// <remarks>
    /// Required on a handler that requires anything of its caller, and meaningless on one that does
    /// not. Like <see cref="Duration"/> it is left at its default rather than assigned in the
    /// constructor, so that "the author did not say" survives composition.
    /// </remarks>
    public CacheScope Scope { get; set; }

    /// <summary>
    /// The names an entry from this handler can be invalidated by.
    /// </summary>
    /// <example>
    /// <code>
    /// [Get("/rates/{symbol}")]
    /// [CacheResponse&lt;VaryByRoute&gt;(Duration = 3600, Tags = ["rates"])]
    /// public Rate Read(string symbol) =&gt; _rates.Latest(symbol);
    ///
    /// // and where a new set is published
    /// await _store.EvictByTag("rates", cancellationToken);
    /// </code>
    /// </example>
    /// <remarks>
    /// A name the declaration chooses, rather than the key the filter composed. The key carries the
    /// handler's method and path, a unit separator, the caller when the scope is per-caller and
    /// then each strategy's part - which is a shape nothing publishes and an application should not
    /// have to rebuild to invalidate its own entries. Composed attributes contribute to one set, in
    /// the order they were declared.
    /// </remarks>
    public string[] Tags { get; set; } = [];

    IReadOnlyList<string> ICacheResponseDeclaration.Tags => Tags;

    public ICacheKeyProvider CreateKeyProvider() => TProvider.Create(Values);

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        if (!Composes(handlerInfo, out var declarations)) {
            yield break;
        }

        // A requirement that reads the request does not run on a hit, and no scope makes it run.
        // Decided here rather than warned about. See the class remarks.
        if (handlerInfo.Requirement?.RequiresContext == true) {
            yield break;
        }

        var filter = ResponseCacheFilter.Compose(handlerInfo, declarations);

        yield return new RequestFilterInfo(_ => filter, FilterOrder.ResponseCache);
    }

    /// <summary>
    /// Whether this attribute is the one that builds the handler's filter, and what it builds it
    /// from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <c>AllowMultiple</c>, a handler carrying three of these is asked three times and needs
    /// one filter over the composite key, not three filters that each look the request up. The
    /// first declaration in the metadata composes them all and the rest stand down. Metadata is
    /// emitted in source order, so the composite is stable across builds rather than dependent on
    /// the order reflection happens to return attributes in.
    /// </para>
    /// <para>
    /// An instance that is not in the metadata was registered globally - see
    /// <c>GlobalFilterServiceCollectionExtensions.AddGlobalFilter</c> - and applies only to a
    /// handler that declares none of its own, so explicit beats convention without the registration
    /// site having to say so.
    /// </para>
    /// </remarks>
    private bool Composes(
        IExecutionRequestHandlerInfo handlerInfo, out IReadOnlyList<ICacheResponseDeclaration> declarations) {
        List<ICacheResponseDeclaration>? declared = null;

        foreach (var item in handlerInfo.Metadata) {
            if (item is ICacheResponseDeclaration declaration) {
                (declared ??= []).Add(declaration);
            }
        }

        if (declared == null) {
            declarations = [this];

            return true;
        }

        declarations = declared;

        return ReferenceEquals(declared[0], this);
    }
}
