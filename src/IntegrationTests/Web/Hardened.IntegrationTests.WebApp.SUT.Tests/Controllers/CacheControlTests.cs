using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// <c>[CacheControl]</c> end to end, on a response rather than in a metadata array.
/// </summary>
/// <remarks>
/// The attribute compiled and travelled all the way to <c>IExecutionRequestHandlerInfo.Metadata</c>
/// for three years without anything reading it, so a route could declare a cache policy and serve
/// responses carrying no <c>Cache-Control</c> at all. The generator tests cover what reaches the
/// metadata; these cover what reaches the client, which is the part that was missing.
/// </remarks>
public class CacheControlTests {

    [HardenedTest]
    public async Task TheDefaultAttributeSendsAPublicMaxAgeOfZero(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cache/default");

        response.Assert.Ok();

        Assert.Equal("public, max-age=0", response.Headers[KnownHeaders.CacheControl]);
    }

    [HardenedTest]
    public async Task AMaxAgeReachesTheResponse(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cache/long");

        response.Assert.Ok();

        Assert.Equal("public, max-age=86400", response.Headers[KnownHeaders.CacheControl]);
    }

    /// <summary>
    /// The flags form, which is what could not even be written before the generator qualified
    /// attribute arguments — the natural spelling of the enum did not compile.
    /// </summary>
    [HardenedTest]
    public async Task ANoStoreHandlerSendsNoStoreAndNoMaxAge(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cache/none");

        response.Assert.Ok();

        Assert.Equal("no-store", response.Headers[KnownHeaders.CacheControl]);
    }

    [HardenedTest]
    public async Task FlagsAndMaxAgeCombine(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cache/private");

        response.Assert.Ok();

        Assert.Equal("private, max-age=60", response.Headers[KnownHeaders.CacheControl]);
    }

    /// <summary>
    /// A handler without the attribute is untouched, so the filter costs nothing where it was not
    /// asked for.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerWithoutTheAttributeSendsNoCacheControl(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cache/unset");

        response.Assert.Ok();

        Assert.False(response.Headers.ContainsKey(KnownHeaders.CacheControl));
    }

    /// <summary>
    /// Declared on the controller, applied to every route on it.
    /// </summary>
    [HardenedTest]
    public async Task AControllerLevelAttributeReachesEveryRouteOnIt(ITestWebApp testWebApp) {
        var one = await testWebApp.Get("/cache-all/one");
        var two = await testWebApp.Get("/cache-all/two");

        one.Assert.Ok();
        two.Assert.Ok();

        Assert.Equal("public, max-age=30", one.Headers[KnownHeaders.CacheControl]);
        Assert.Equal("public, max-age=30", two.Headers[KnownHeaders.CacheControl]);
    }
}
