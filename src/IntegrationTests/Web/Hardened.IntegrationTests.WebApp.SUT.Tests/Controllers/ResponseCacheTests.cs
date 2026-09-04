using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// <c>[CacheResponse&lt;T&gt;]</c> end to end, where a hit is the handler not running.
/// </summary>
/// <remarks>
/// Each handler answers with a counter that only advances when it runs, so the same body twice is
/// the assertion. Every test gets its own service provider, and therefore its own store.
/// </remarks>
public class ResponseCacheTests {

    [HardenedTest]
    public async Task ASecondRequestIsAnsweredWithoutRunningTheHandler(ITestWebApp testWebApp) {
        var first = await testWebApp.Get("/response-cache/catalog?culture=en-GB");
        var second = await testWebApp.Get("/response-cache/catalog?culture=en-GB");

        first.Assert.Ok();
        second.Assert.Ok();

        Assert.Equal("en-GB-1", first.Deserialize<string>());
        Assert.Equal("en-GB-1", second.Deserialize<string>());
    }

    /// <summary>
    /// The value the strategy was named on is in the key, so a different one is a different entry.
    /// </summary>
    [HardenedTest]
    public async Task ADifferentQueryValueIsADifferentEntry(ITestWebApp testWebApp) {
        await testWebApp.Get("/response-cache/catalog?culture=en-GB");

        var other = await testWebApp.Get("/response-cache/catalog?culture=fr-FR");

        Assert.Equal("fr-FR-2", other.Deserialize<string>());
    }

    /// <summary>
    /// A handler that declares nothing is untouched, so the filter costs nothing where it was not
    /// asked for.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerThatDeclaresNothingIsNotCached(ITestWebApp testWebApp) {
        await testWebApp.Get("/response-cache/uncached");

        var second = await testWebApp.Get("/response-cache/uncached");

        Assert.Equal("2", second.Deserialize<string>());
    }

    /// <summary>
    /// Two strategies compose into one key. Changing either half misses; changing neither hits.
    /// </summary>
    [HardenedTest]
    public async Task ComposedStrategiesBothCount(ITestWebApp testWebApp) {
        var first = await testWebApp.Get(
            "/response-cache/composed?culture=en-GB", Language("en-GB"));

        var repeat = await testWebApp.Get(
            "/response-cache/composed?culture=en-GB", Language("en-GB"));

        var otherHeader = await testWebApp.Get(
            "/response-cache/composed?culture=en-GB", Language("fr-FR"));

        Assert.Equal("en-GB-1", first.Deserialize<string>());
        Assert.Equal("en-GB-1", repeat.Deserialize<string>());
        Assert.Equal("en-GB-2", otherHeader.Deserialize<string>());
    }

    /// <summary>
    /// <c>VaryByHeader</c> writes the header it varies on, so a shared cache in front of this
    /// service does not serve one caller's answer to another.
    /// </summary>
    [HardenedTest]
    public async Task AVariedResponseSaysWhatItVariedOn(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/response-cache/composed?culture=en-GB", Language("en-GB"));

        // Merged with the Accept-Encoding the compression filter adds, rather than assigned.
        Assert.Contains("Accept-Language", response.Headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>
    /// The cache runs before authorization that reads the request, so a handler guarded by one is
    /// not cached at all. ASP.NET Core ships this as a note in its documentation and the failure is
    /// silent.
    /// </summary>
    [HardenedTest]
    public async Task AResourceScopedHandlerIsNotCached(ITestWebApp testWebApp) {
        await testWebApp.Get("/response-cache/owned/7");

        var second = await testWebApp.Get("/response-cache/owned/7");

        Assert.Equal("7-2", second.Deserialize<string>());
    }

    /// <summary>
    /// A requirement over grants alone settles ahead of the cache, so it is still cached. The
    /// resource-scoped rule has to refuse the case above without also refusing this one.
    /// </summary>
    [HardenedTest]
    public async Task AGrantGuardedHandlerIsStillCached(ITestWebApp testWebApp) {
        await testWebApp.Get("/response-cache/granted", Grants("pets:read"));

        var second = await testWebApp.Get("/response-cache/granted", Grants("pets:read"));

        Assert.Equal("1", second.Deserialize<string>());
    }

    /// <summary>
    /// The question no test in this repository asked until the 0.19.0-rc1000 trial: what a caller
    /// the guard refuses gets from a cache a permitted caller filled.
    /// </summary>
    /// <remarks>
    /// It got the stored 200 and the body with it. The refusal is recorded ahead of this stage and
    /// written behind it, so the cache has to read it rather than treat "still travelling" as "still
    /// permitted". <c>AGrantGuardedHandlerIsStillCached</c> above is the other half: warming and
    /// reading as a permitted caller was all that was ever exercised.
    /// </remarks>
    [HardenedTest]
    public async Task AWarmCacheStillRefusesTheGrantlessCaller(ITestWebApp testWebApp) {
        var warm = await testWebApp.Get("/response-cache/granted", Grants("pets:read"));

        warm.Assert.Ok();

        var grantless = await testWebApp.Get("/response-cache/granted");

        Assert.True(grantless.StatusCode is 401 or 403,
            $"a grantless caller was answered {grantless.StatusCode} from the warm cache");
    }

    /// <summary>
    /// And the refused caller is answered the refusal rather than nothing, which is what recording
    /// it and continuing buys over short-circuiting here.
    /// </summary>
    [HardenedTest]
    public async Task TheRefusedCallerIsNotGivenTheStoredBody(ITestWebApp testWebApp) {
        var warm = await testWebApp.Get("/response-cache/granted", Grants("pets:read"));
        var grantless = await testWebApp.Get("/response-cache/granted");

        Assert.NotEqual(await warm.ReadTextAsync(), await grantless.ReadTextAsync());
    }

    /// <summary>
    /// The defect all three trial arms found: a second subscriber served the first subscriber's
    /// row, with a 200.
    /// </summary>
    /// <remarks>
    /// The guard the framework read was <c>Requirement.RequiresContext</c>, true only for a
    /// requirement built from a predicate. An ownership check written as handler code answering 404
    /// - which is what a description forces, because it can require authentication and cannot
    /// require ownership - was invisible to it. <c>CacheScope.PerCaller</c> is the declaration that
    /// says so, and it keys the entry on the caller.
    /// </remarks>
    [HardenedTest]
    public async Task AnOwnerScopedHandlerAnswersEachCallerTheirOwn(ITestWebApp testWebApp) {
        var first = await testWebApp.Get(
            "/response-cache/owned-by-subject", Caller("pets:read", "subscriber-one"));

        var second = await testWebApp.Get(
            "/response-cache/owned-by-subject", Caller("pets:read", "subscriber-two"));

        Assert.Equal("subscriber-one-1", first.Deserialize<string>());
        Assert.Equal("subscriber-two-2", second.Deserialize<string>());
    }

    /// <summary>
    /// And each caller's own entry is still an entry, so the feature survives being made safe.
    /// </summary>
    [HardenedTest]
    public async Task AnOwnerScopedHandlerStillAnswersOneCallerFromTheStore(ITestWebApp testWebApp) {
        await testWebApp.Get(
            "/response-cache/owned-by-subject", Caller("pets:read", "subscriber-one"));

        var repeat = await testWebApp.Get(
            "/response-cache/owned-by-subject", Caller("pets:read", "subscriber-one"));

        Assert.Equal("subscriber-one-1", repeat.Deserialize<string>());
    }

    /// <summary>
    /// A guarded handler that says nothing about who may be served its answer fails naming itself,
    /// rather than picking one of the two readings of silence.
    /// </summary>
    /// <remarks>
    /// Raised as the handler's filter chain is built, which is the first request its route matches -
    /// so it reaches a test as the exception and a running host as a logged 500. That is where the
    /// other two failures a declaration can express are raised, and for the same reason: it names
    /// the handler and is asked once rather than per request.
    /// </remarks>
    [HardenedTest]
    public async Task AGuardedHandlerThatStatesNoScopeFails(ITestWebApp testWebApp) {
        var failure = await Assert.ThrowsAsync<CacheScopeUndeclaredException>(
            () => testWebApp.Get(
                "/response-cache/unstated-scope", Caller("pets:read", "subscriber-one")));

        Assert.Equal("GET /response-cache/unstated-scope", failure.Handler);
        Assert.Contains("CacheScope.PerCaller", failure.Message);
    }

    /// <summary>
    /// A published change reaches a cached read, which the trial found was impossible: an
    /// application could not reach its own entries at all, so an hour-long entry meant an hour.
    /// </summary>
    [HardenedTest]
    public async Task APublishReachesACachedRead(ITestWebApp testWebApp) {
        var first = await testWebApp.Get("/response-cache/tagged");
        var cached = await testWebApp.Get("/response-cache/tagged");

        await testWebApp.Post("", "/response-cache/publish");

        var afterPublish = await testWebApp.Get("/response-cache/tagged");

        Assert.Equal("1", first.Deserialize<string>());
        Assert.Equal("1", cached.Deserialize<string>());
        Assert.Equal("2", afterPublish.Deserialize<string>());
    }

    private static Action<TestWebRequest> Language(string value) =>
        request => request.Headers["Accept-Language"] = new StringValues(value);

    private static Action<TestWebRequest> Grants(string value) =>
        request => request.Headers[TestGrantsPrincipalSource.GrantsHeader] = new StringValues(value);

    /// <summary>Which caller, for the tests where one caller's data reaching another is the point.</summary>
    private static Action<TestWebRequest> Caller(string grants, string subject) =>
        request => {
            request.Headers[TestGrantsPrincipalSource.GrantsHeader] = new StringValues(grants);
            request.Headers[TestGrantsPrincipalSource.SubjectHeader] = new StringValues(subject);
        };
}
