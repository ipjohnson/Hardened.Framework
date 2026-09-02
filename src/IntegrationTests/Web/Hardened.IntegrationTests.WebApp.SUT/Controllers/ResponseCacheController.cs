using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Authorization;
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

    public ResponseCacheController(HandlerCallCounter counter) {
        _counter = counter;
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
    /// Guarded by grants alone, which settle before serialization and so ahead of the cache. Safe
    /// to cache behind, and the case the resource-scoped rule must not also refuse.
    /// </summary>
    [Get("/granted")]
    [AuthorizeGrants("pets:read")]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    public string Granted() => _counter.Next("granted").ToString();
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
