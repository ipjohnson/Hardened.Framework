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
    /// The lambda closes over the tenant, which is the whole reason the closure has to survive into
    /// the handler. Nothing resolves it and nothing could have replaced it with a call to a method.
    /// </remarks>
    [HardenedTest]
    public async Task ALambdaRegistrationAnswers(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/ping/7");

        Assert.Equal(200, response.StatusCode);

        var order = response.Deserialize<Order>();

        Assert.Equal(7, order.Id);
        Assert.Equal("acme", order.Tenant);
    }

    /// <remarks>
    /// One lambda, registered once per tenant, closing over a different value each time.
    /// </remarks>
    [HardenedTest]
    public async Task EachRegistrationKeepsItsOwnClosure(ITestWebApp app)
    {
        Assert.Equal(
            "acme",
            (await app.Get("/registered/acme/ping/1")).Deserialize<Order>().Tenant
        );
        Assert.Equal(
            "globex",
            (await app.Get("/registered/globex/ping/1")).Deserialize<Order>().Tenant
        );
    }

    /// <remarks>
    /// The token is bound by name from the registered template, the same way the controller form
    /// binds - which is what the type-first classification has to agree with.
    /// </remarks>
    [HardenedTest]
    public async Task ALambdaParameterBindsFromThePath(ITestWebApp app)
    {
        Assert.Equal(42, (await app.Get("/registered/acme/ping/42")).Deserialize<Order>().Id);
    }

    /// <remarks>
    /// A service parameter and a body parameter, classified by type because there is no template to
    /// classify them against. Neither is a path token, and both arrive.
    /// </remarks>
    [HardenedTest]
    public async Task ALambdaTakesAServiceAndABody(ITestWebApp app)
    {
        var response = await app.Post(new Order(9, "ignored"), "/registered/acme/echo");

        Assert.Equal(200, response.StatusCode);

        var order = response.Deserialize<Order>();

        Assert.Equal(9, order.Id);
        Assert.Equal("acme:any", order.Tenant);
    }

    /// <remarks>
    /// Map names its verb in the call, which has to be a constant: the verb is written into the
    /// handler's own information and into the table the route joins.
    /// </remarks>
    [HardenedTest]
    public async Task MapRegistersTheVerbItNames(ITestWebApp app)
    {
        Assert.Equal(200, (await app.Delete("/registered/acme/orders/3")).StatusCode);
    }

    [HardenedTest]
    public async Task AConstraintGuardsALambdaRouteToo(ITestWebApp app)
    {
        Assert.Equal(404, (await app.Get("/registered/acme/ping/not-a-number")).StatusCode);
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

    /// <summary>
    /// The media type the lambda declares is what it answers under. Without it a string is a JSON
    /// string, quotes included, which is what this route answered while the attribute went unread.
    /// </summary>
    [HardenedTest]
    public async Task AMediaTypeDeclaredOnALambdaReachesTheWire(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/label/7");

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith("text/plain", response.Headers["Content-Type"].ToString());
        Assert.Equal("7:acme", await response.ReadTextAsync());
    }

    /// <summary>
    /// The view writes the response, and the model it was given does not go out beside it.
    /// </summary>
    /// <remarks>
    /// <c>OrderCard</c> writes the id alone. The tenant is in the model and not on the page, so a
    /// response carrying it is the serializer answering in the view's place.
    /// </remarks>
    [HardenedTest]
    public async Task AViewNamedOnALambdaWritesTheResponse(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/card/7");

        var body = await response.ReadTextAsync();

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith("text/html", response.Headers["Content-Type"].ToString());
        Assert.Equal("<p>7</p>", body);
        Assert.DoesNotContain("acme", body);
    }

    /// <remarks>
    /// An output takes the response out of negotiation: a client asking for JSON is answered the
    /// page rather than the model, because falling back to the model is what would disclose it.
    /// </remarks>
    [HardenedTest]
    public async Task AViewAnswersACallerAskingForJson(ITestWebApp app)
    {
        var response = await app.Get(
            "/registered/acme/card/7",
            request => request.Headers["Accept"] = "application/json"
        );

        Assert.Equal("<p>7</p>", await response.ReadTextAsync());
    }
}
