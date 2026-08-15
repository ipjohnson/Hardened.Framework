namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Two routes sharing a prefix, with a path token in the same position under different
/// names. BindingController declares:
///
///     [Get("/path/{id}")]                  FromPath
///     [Get("/path/{first}/{second}")]      OverlappingPathTokens
///
/// The route tree shares one node for that token position, so the node cannot know which
/// name applies. It used to take the name from whichever route registered first, which meant
/// /binding/path/a/b routed correctly to OverlappingPathTokens and then failed binding with
/// "first was missing" - a 400 that only appeared when the route was called.
///
/// Token names now belong to the route rather than the tree node: the matched leaf supplies
/// them and values are filled in positionally as the match unwinds.
///
/// This shape is ordinary REST - /users/{id} alongside /users/{userId}/posts/{postId} - and
/// the old behaviour was triggered by the rename that makes the second route clearer.
/// </summary>
public class OverlappingRouteTokenNamesTests {

    [HardenedTest]
    public async Task OverlappingRoutesBindTheirOwnTokenNames(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/alpha/beta");

        response.Assert.Ok();
        Assert.Equal("alpha:beta", response.Deserialize<string>());
    }

    /// <summary>
    /// A path deeper than either route declares matches neither, over the whole host rather than
    /// against the routing table alone.
    ///
    /// <para>
    /// Both of these used to return 200: a token took the rest of the path, so
    /// <c>/binding/path/a/b/c</c> reached <c>OverlappingPathTokens</c> with
    /// <c>second = "b/c"</c>, and <c>/binding/path-typed/1/2/3</c> reached the typed handler with
    /// <c>"1/2/3"</c> and failed conversion — a 400, which reads as a bad request rather than a
    /// path that was never declared.
    /// </para>
    /// </summary>
    [HardenedTest]
    public async Task APathDeeperThanAnyRouteIsNotFound(ITestWebApp testWebApp) {
        (await testWebApp.Get("/binding/path/a/b/c")).Assert.NotFound();
        (await testWebApp.Get("/binding/path-typed/1/2/3")).Assert.NotFound();
    }

    /// <summary>
    /// The shorter route keeps working. It always did, which is why the defect went
    /// unnoticed: whichever route registered first behaved correctly.
    /// </summary>
    [HardenedTest]
    public async Task TheFirstRegisteredOverlappingRouteStillBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/only-one");

        response.Assert.Ok();
        Assert.Equal("only-one", response.Deserialize<string>());
    }

    /// <summary>
    /// The deeper route's second token is unambiguous - only one route reaches that position
    /// - so it was never affected. Asserted so a future change cannot regress it silently.
    /// </summary>
    [HardenedTest]
    public async Task DeeperUnsharedTokenStillBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/pair/one/two");

        response.Assert.Ok();
        Assert.Equal("one:two", response.Deserialize<string>());
    }
}
