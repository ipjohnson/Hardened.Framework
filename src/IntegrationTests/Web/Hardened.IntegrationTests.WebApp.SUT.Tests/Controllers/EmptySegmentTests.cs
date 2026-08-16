namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A token names at least one character.
/// </summary>
/// <remarks>
/// <para>
/// The routing guide's rule is that a token matches exactly one segment. The empty string left
/// after a trailing slash is not a segment, and neither is the nothing between the two slashes of
/// <c>//</c> — but both used to match, binding the token to <c>""</c> and reaching the handler's
/// binder, which answered 400.
/// </para>
/// <para>
/// The status is the point. 400 tells a client it addressed a real endpoint incorrectly; 404 says
/// there is no resource at that URL. Only the second is true here, and the two are cached
/// differently by API Gateway and CloudFront and read differently by a generated client.
/// </para>
/// </remarks>
public class EmptySegmentTests {

    [HardenedTest]
    public async Task ATrailingSlashDoesNotFillASingleSegmentToken(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/");

        response.Assert.NotFound();
    }

    /// <summary>
    /// The same rule at a token that is not the last thing in the route, where the empty match
    /// came from accepting a boundary at the position the scan started from.
    /// </summary>
    [HardenedTest]
    public async Task AnEmptySegmentDoesNotFillAMidRouteToken(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/pair//second");

        response.Assert.NotFound();
    }

    /// <summary>
    /// A catch-all means the rest of the path, and there is no rest here.
    /// </summary>
    [HardenedTest]
    public async Task ATrailingSlashDoesNotFillACatchAllToken(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/files/");

        response.Assert.NotFound();
    }

    /// <summary>
    /// The point of the guard is to reject nothing, not to reject something — a token with a real
    /// value still binds, including a catch-all spanning separators.
    /// </summary>
    [HardenedTest]
    public async Task ATokenWithAValueStillBinds(ITestWebApp testWebApp) {
        var single = await testWebApp.Get("/binding/path/abc");

        single.Assert.Ok();
        Assert.Equal("abc", single.Deserialize<string>());

        var catchAll = await testWebApp.Get("/binding/files/img/logo.png");

        catchAll.Assert.Ok();
        Assert.Equal("img/logo.png", catchAll.Deserialize<string>());
    }
}
