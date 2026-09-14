namespace Hardened.IntegrationTests.RegisteredRoutes.SUT.Tests;

/// <summary>
/// Routes registered at startup, driven through the real pipeline.
/// </summary>
/// <remarks>
/// The generator suite proves the catalog is emitted and the unit tests prove the matcher matches.
/// This proves the part neither can: that a path computed at run time reaches a generated handler,
/// binds its tokens, and comes back through the same filter chain, serializer and status handling
/// an attribute route does.
/// </remarks>
public class RegisteredRouteTests
{
    [HardenedTest]
    public async Task TheAttributeRouteStillAnswers(ITestWebApp app)
    {
        var response = await app.Get("/registered/orders/7");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(7, response.Deserialize<Order>().Id);
    }

    [HardenedTest]
    public async Task ARegisteredPathReachesTheSameHandler(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/orders/7");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(7, response.Deserialize<Order>().Id);
    }

    [HardenedTest]
    public async Task EveryRegisteredPathAnswers(ITestWebApp app)
    {
        Assert.Equal(200, (await app.Get("/registered/acme/orders/1")).StatusCode);
        Assert.Equal(200, (await app.Get("/registered/globex/orders/2")).StatusCode);
    }

    /// <remarks>
    /// The token is bound by name rather than by position, which is what lets a handler compiled
    /// for <c>/orders/{id}</c> answer at <c>/acme/orders/{id}</c> without being told.
    /// </remarks>
    [HardenedTest]
    public async Task TheTokenBindsFromTheRegisteredTemplate(ITestWebApp app)
    {
        var response = await app.Get("/registered/globex/orders/42");

        Assert.Equal(42, response.Deserialize<Order>().Id);
    }

    [HardenedTest]
    public async Task AConstraintOnARegisteredPathStillGuards(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/orders/not-a-number");

        Assert.Equal(404, response.StatusCode);
    }

    [HardenedTest]
    public async Task ARegisteredCatchAllTakesTheRestOfThePath(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/files/css/site.css");

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("css/site.css", await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task ARegisteredRouteAnswers405LikeAnyOther(ITestWebApp app)
    {
        var response = await app.Delete("/registered/acme/orders");

        Assert.Equal(405, response.StatusCode);
        Assert.Contains("POST", response.Headers["Allow"].ToString());
    }

    [HardenedTest]
    public async Task ARegisteredBodyRouteDeserializesItsBody(ITestWebApp app)
    {
        var response = await app.Post(new Order(3, "acme"), "/registered/acme/orders");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(3, response.Deserialize<Order>().Id);
    }

    [HardenedTest]
    public async Task APathNobodyRegisteredIsStillANotFound(ITestWebApp app)
    {
        var response = await app.Get("/registered/nowhere/orders/1");

        Assert.Equal(404, response.StatusCode);
    }

    /// <remarks>
    /// The entry point implements the interface itself, which is the shape that needs no class of
    /// its own. Nothing puts an entry point in the container, so this only answers because the
    /// generator registers every implementer it finds.
    /// </remarks>
    [HardenedTest]
    public async Task TheEntryPointCanRegisterRoutesItself(ITestWebApp app)
    {
        var response = await app.Get("/registered/module/orders/5");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(5, response.Deserialize<Order>().Id);
    }

    /// <remarks>
    /// <c>TenantRoutes</c> carries no <c>[SingletonService]</c>. Implementing the interface is the
    /// whole declaration.
    /// </remarks>
    [HardenedTest]
    public async Task AnUnattributedRegistrationStillRuns(ITestWebApp app)
    {
        Assert.Equal(200, (await app.Get("/registered/acme/orders/1")).StatusCode);
    }

    /// <remarks>
    /// A handler registered at three paths reports the path it answered at, not the one it was
    /// declared with. Everything that reads <c>IExecutionRequestHandlerInfo.Path</c> - a filter, an
    /// authorization convention, a log line - depends on that.
    /// </remarks>
    [HardenedTest]
    public async Task TheHandlerReportsThePathItAnsweredAt(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/orders/1");

        Assert.Equal(200, response.StatusCode);
    }
}
