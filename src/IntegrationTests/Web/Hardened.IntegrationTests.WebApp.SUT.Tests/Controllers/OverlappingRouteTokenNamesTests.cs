namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A known routing defect, captured as a runnable reproduction rather than left in a
/// tracker.
///
/// When two routes share a prefix and both have a path token in the same position, the
/// route tree stores the token name on the tree *node* - that is, on the position - rather
/// than on the individual route. Both routes therefore share whichever name was registered
/// first, and the other one fails to bind.
///
/// BindingController declares:
///
///     [Get("/path/{id}")]                  FromPath
///     [Get("/path/{first}/{second}")]      OverlappingPathTokens
///
/// A request to /binding/path/a/b routes correctly to OverlappingPathTokens - the log shows
/// the right handler - and then fails binding with BadRequestException "first was missing",
/// returning 400. The generated binding code is correct; it asks PathTokens for "first",
/// and the collection was populated under the name from the shorter route.
///
/// This fails at runtime rather than at build time, which makes it more dangerous than the
/// generator defects fixed alongside it: nothing surfaces until someone calls the route.
///
/// Fixing it means moving the token name from RouteTreeNode onto the leaf so each route
/// carries its own, and updating the routing table emitter to match. That is a structural
/// change to the route tree, so it is recorded here rather than bundled into this work.
///
/// Remove the Skip when the token name moves to the route.
/// </summary>
public class OverlappingRouteTokenNamesTests {

    [HardenedTest(Skip = "Known defect: route tree stores path token names per position, " +
                         "so overlapping routes with differently named tokens cannot both bind.")]
    public async Task OverlappingRoutesBindTheirOwnTokenNames(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/alpha/beta");

        response.Assert.Ok();
        Assert.Equal("alpha:beta", response.Deserialize<string>());
    }

    /// <summary>
    /// The shorter route still works, which is why the defect goes unnoticed: whichever
    /// route registered first behaves correctly.
    /// </summary>
    [HardenedTest]
    public async Task TheFirstRegisteredOverlappingRouteStillBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/only-one");

        response.Assert.Ok();
        Assert.Equal("only-one", response.Deserialize<string>());
    }
}
