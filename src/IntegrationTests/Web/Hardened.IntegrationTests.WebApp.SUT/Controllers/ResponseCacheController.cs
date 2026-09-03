using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// <c>[CacheResponse&lt;T&gt;]</c> through the real pipeline, where a hit is the handler not
/// running.
/// </summary>
/// <remarks>
/// Every handler here returns a number that only changes when the handler runs, so a test asserts
/// what a cache is for rather than what it produced. A unit test over the filter can only prove the
/// filter is consistent with itself; whether the generator carries a generic attribute with both a
/// positional argument and a named property into the handler's metadata is a question only a built
/// application answers.
/// </remarks>
[BasePath("/response-cache")]
public class ResponseCacheController {

    private readonly HandlerCallCounter _counter;
    private readonly ICurrentCaller _currentCaller;
    private readonly IResponseCacheStore _store;

    public ResponseCacheController(
        HandlerCallCounter counter, ICurrentCaller currentCaller, IResponseCacheStore store) {
        _counter = counter;
        _currentCaller = currentCaller;
        _store = store;
    }

    [Get("/catalog")]
    [CacheResponse<VaryByQuery>("culture", Duration = 60)]
    public string Catalog([FromQueryString] string culture) => culture + "-" + _counter.Next("catalog");

    /// <summary>A handler that declares nothing, so nothing about it changes.</summary>
    [Get("/uncached")]
    public string Uncached() => _counter.Next("uncached").ToString();

    /// <summary>
    /// Two strategies on one handler, which needs <c>AllowMultiple</c> and is what
    /// <c>[OutputCache]</c> cannot express at all.
    /// </summary>
    [Get("/composed")]
    [CacheResponse<VaryByQuery>("culture")]
    [CacheResponse<VaryByHeader>("Accept-Language", Duration = 60)]
    public string Composed([FromQueryString] string culture) => culture + "-" + _counter.Next("composed");

    /// <summary>
    /// Guarded by a requirement that reads the request, which runs after the cache would have
    /// answered. The framework declines to install the filter rather than serving one caller's
    /// answer to another.
    /// </summary>
    [Get("/owned/{ownerId}")]
    [OwnedByCaller]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    public string Owned(string ownerId) => ownerId + "-" + _counter.Next("owned");

    /// <summary>
    /// Guarded by grants alone, which settle before serialization and so ahead of the cache, and
    /// the case the resource-scoped rule must not also refuse.
    /// </summary>
    /// <remarks>
    /// This comment used to say "safe to cache behind" and stopped there, which is how the
    /// authorization bypass shipped. Settling ahead of the cache is not by itself what makes it
    /// safe: a refusal ahead of serialization is <em>recorded</em> and travels on, so the cache has
    /// to read it. <c>AWarmCacheStillRefusesTheGrantlessCaller</c> is the test that says so.
    /// </remarks>
    [Get("/granted")]
    [AuthorizeGrants("pets:read")]
    [CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.AllCallers)]
    public string Granted() => _counter.Next("granted").ToString();

    /// <summary>
    /// The shape three trial arms found one caller's data in: an authenticated read whose ownership
    /// check is the handler's own code, cached.
    /// </summary>
    /// <remarks>
    /// Nothing on the handler distinguishes this from <see cref="Granted"/>. A description can say
    /// "the caller must be authenticated" and cannot say "and the row must be theirs", so the check
    /// is here, answering 404 rather than 403 so the row's existence is not disclosed - and that is
    /// invisible to anything reading the requirement. <c>CacheScope.PerCaller</c> is the author
    /// saying what the metadata cannot.
    /// </remarks>
    [Get("/owned-by-subject")]
    [AuthorizeGrants("pets:read")]
    [CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.PerCaller)]
    public string OwnedBySubject() =>
        _currentCaller.Principal.Subject + "-" + _counter.Next("owned-by-subject");

    /// <summary>
    /// The same read with nothing said about who may be served it, so the first request to it
    /// fails naming the handler rather than serving one caller's answer to another.
    /// </summary>
    [Get("/unstated-scope")]
    [AuthorizeGrants("pets:read")]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    public string UnstatedScope() =>
        _currentCaller.Principal.Subject + "-" + _counter.Next("unstated-scope");

    /// <summary>
    /// A read whose entry outlives anything worth waiting for, invalidated by name when the thing
    /// it read changes.
    /// </summary>
    /// <remarks>
    /// The pair is the whole feature. Without <see cref="Publish"/> an application's only way to
    /// reach its own entries was to expire them, so a published change appeared within the cache
    /// lifetime and never sooner.
    /// </remarks>
    [Get("/tagged")]
    [CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["catalog"])]
    public string Tagged() => _counter.Next("tagged").ToString();

    /// <summary>What an application does where it changes what a cached read reads.</summary>
    [Post("/publish")]
    public async Task<string> Publish(CancellationToken cancellationToken) {
        await _store.EvictByTag("catalog", cancellationToken);

        return "published";
    }
}

/// <summary>
/// A requirement over the request rather than over grants, admitting everyone.
/// </summary>
/// <remarks>
/// It passes so that what the test observes is whether the response was cached, not whether the
/// request was allowed. <see cref="Requirement.RequiresContext"/> is what the cache reads, and a
/// predicate is the smallest requirement that sets it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OwnedByCallerAttribute : Attribute, IAuthorizeAttribute {

    public Requirement Requirement { get; } =
        Requirement.Predicate((_, _) => true, "the caller owns this record");
}
