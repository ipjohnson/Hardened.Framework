namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A base path that is itself a resource.
/// </summary>
/// <remarks>
/// <c>[BasePath("/collection")]</c> plus <c>[Get("/")]</c> composed by concatenation into
/// <c>/collection/</c>, so the URL the controller declares answered nothing and only the
/// trailing-slash spelling worked. Trailing slashes are significant in Hardened and the default
/// policy is strict, so that was not a spelling a client could be expected to guess — it was a
/// collection endpoint that did not exist at its own address.
/// </remarks>
public class CollectionRootTests {

    [HardenedTest]
    public async Task TheCollectionAnswersAtItsBasePath(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/collection");

        response.Assert.Ok();
        Assert.Equal("collection", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task TheCollectionAnswersEveryVerbAtItsBasePath(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("", "/collection");

        response.Assert.Ok();
        Assert.Equal("created", response.Deserialize<string>());
    }

    /// <summary>
    /// The boundary slash is collapsed, not deleted — a token route under the same base still
    /// composes with its separator.
    /// </summary>
    [HardenedTest]
    public async Task ASiblingTokenRouteIsUnaffected(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/collection/42");

        response.Assert.Ok();
        Assert.Equal("item:42", response.Deserialize<string>());
    }

    /// <summary>
    /// The other spelling is a different URL, which is what strict trailing-slash matching means
    /// and what an OpenAPI document describes. Asserted so that collapsing the boundary does not
    /// quietly start answering both.
    /// </summary>
    /// <remarks>
    /// Asserted as "not the collection" rather than as a status, because the status this currently
    /// produces is a separate defect: <c>/collection/</c> reaches <c>/collection/{id}</c> with an
    /// empty segment bound to <c>id</c>, and comes back 400 from the binder rather than 404. A
    /// token should not match an empty segment — the routing guide's own rule is that a token
    /// matches exactly one segment, and the trailing empty string after a final slash is not one.
    /// That is its own fix in the route tree and does not belong in this one, so this test pins
    /// only what the base-path composition is responsible for.
    /// </remarks>
    [HardenedTest]
    public async Task TheTrailingSlashSpellingIsADifferentUrl(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/collection/");

        Assert.NotEqual(200, response.StatusCode);
    }
}
