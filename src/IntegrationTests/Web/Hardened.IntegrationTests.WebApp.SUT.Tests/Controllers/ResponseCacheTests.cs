using Hardened.Requests.Abstract.Headers;
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

        Assert.Equal("Accept-Language", response.Headers[KnownHeaders.Vary]);
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

    private static Action<TestWebRequest> Language(string value) =>
        request => request.Headers["Accept-Language"] = new StringValues(value);

    private static Action<TestWebRequest> Grants(string value) =>
        request => request.Headers["X-Test-Grants"] = new StringValues(value);
}
