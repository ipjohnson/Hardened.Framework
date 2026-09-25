using System.Text.Json;

namespace Hardened.IntegrationTests.RegisteredRoutes.SUT.Tests;

/// <summary>
/// The document the application serves, with the routes it registered at startup in it.
/// </summary>
/// <remarks>
/// Nothing writes an OpenAPI operation at run time. Every registered route's operation is fully
/// determined at build time except its path key, so the build writes the operation and the
/// application writes the path around it, once, when registration closes.
/// </remarks>
public class RegisteredRouteDocumentTests
{
    private static async Task<JsonElement> Paths(ITestWebApp app)
    {
        var response = await app.Get("/openapi.json");

        Assert.Equal(200, response.StatusCode);

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        return document.RootElement.GetProperty("paths").Clone();
    }

    [ModuleTest]
    public async Task TheDeclaredRoutesAreStillThere(ITestWebApp app)
    {
        var paths = await Paths(app);

        Assert.True(paths.TryGetProperty("/registered/orders/{id}", out _));
    }

    /// <remarks>
    /// One entry per tenant, from a list that did not exist when the document was written.
    /// </remarks>
    [ModuleTest]
    public async Task ARegisteredControllerRouteIsDescribed(ITestWebApp app)
    {
        var paths = await Paths(app);

        Assert.True(paths.TryGetProperty("/registered/acme/orders/{id}", out var acme));
        Assert.True(acme.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/registered/globex/orders/{id}", out _));
    }

    [ModuleTest]
    public async Task ARegisteredLambdaIsDescribed(ITestWebApp app)
    {
        var paths = await Paths(app);

        Assert.True(paths.TryGetProperty("/registered/acme/ping/{id}", out var ping));
        Assert.True(ping.TryGetProperty("get", out _));
    }

    /// <remarks>
    /// The constraint is a routing concern the document has no way to express, so the path key is
    /// the token's name alone - which is what every client generator reads.
    /// </remarks>
    [ModuleTest]
    public async Task ThePathKeyCarriesNoConstraint(ITestWebApp app)
    {
        var paths = await Paths(app);

        foreach (var path in paths.EnumerateObject())
        {
            Assert.DoesNotContain(":", path.Name);
        }
    }

    /// <remarks>
    /// Two verbs at one path are one path item with two operations, which is what the grouping at
    /// registration is for.
    /// </remarks>
    [ModuleTest]
    public async Task TwoVerbsAtOnePathAreOnePathItem(ITestWebApp app)
    {
        var paths = await Paths(app);

        var orders = paths.GetProperty("/registered/acme/orders/{id}");

        Assert.True(orders.TryGetProperty("get", out _));
        Assert.True(orders.TryGetProperty("delete", out _));
    }

    /// <summary>
    /// The prose a handler documents itself with, through the splice and back out intact.
    /// </summary>
    /// <remarks>
    /// <c>TenantController.Get</c> carries a double quote and a backslash for this. The catalog
    /// writes its operation into a C# string literal, and 0.36.0-rc1000 escaped the quote and not
    /// the backslash: a quote ended the literal, so an application declaring an
    /// <c>IRouteRegistration</c> did not compile at all, and a backslash reached the served
    /// document as a lone escape that <c>JsonDocument.Parse</c> refuses. Every test on this
    /// class would fail on the second of those; this one says which defect it is.
    /// </remarks>
    [ModuleTest]
    public async Task ADocCommentsQuoteAndBackslashSurviveTheSplice(ITestWebApp app)
    {
        var paths = await Paths(app);

        var description = paths
            .GetProperty("/registered/acme/orders/{id}")
            .GetProperty("get")
            .GetProperty("description")
            .GetString();

        Assert.Contains("\"null\"", description);
        Assert.Contains("C:\\orders", description);
    }

    /// <summary>
    /// Every operation in the document has an id, and no two share one.
    /// </summary>
    /// <remarks>
    /// The registry writes a registered route's id from the verb and the path, because one
    /// registration serves every path it is registered at and a build-time id would be one id on
    /// several operations. Before that, every lambda published <c>funcInvoke</c> - eighteen times
    /// in the 0.36 trial - and a declared handler registered at three paths published its own id
    /// three times.
    /// </remarks>
    [ModuleTest]
    public async Task EveryOperationIdIsUnique(ITestWebApp app)
    {
        var paths = await Paths(app);

        var ids = paths
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The path a route registered at is what names it, so the same handler at two paths is two
    /// operations a client can tell apart.
    /// </summary>
    [ModuleTest]
    public async Task ARegisteredRouteIsNamedAfterThePathItRegisteredAt(ITestWebApp app)
    {
        var paths = await Paths(app);

        string Id(string path, string method) =>
            paths.GetProperty(path).GetProperty(method).GetProperty("operationId").GetString()!;

        Assert.Equal("registeredAcmeOrdersByIdGet", Id("/registered/acme/orders/{id}", "get"));
        Assert.Equal("registeredGlobexOrdersByIdGet", Id("/registered/globex/orders/{id}", "get"));

        // The lambda form, which used to be funcInvoke whatever it was registered at.
        Assert.Equal("registeredAcmePingByIdGet", Id("/registered/acme/ping/{id}", "get"));
    }

    /// <summary>
    /// A lambda's body reaches the document. Its schema was never read, so
    /// <c>routes.Post(path, (Order body) =&gt; ...)</c> published nothing about the body it
    /// requires and a generated client had no parameter to send one with.
    /// </summary>
    [ModuleTest]
    public async Task ALambdaPublishesTheBodyItReads(ITestWebApp app)
    {
        var paths = await Paths(app);

        var schema = paths
            .GetProperty("/registered/acme/echo")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        Assert.Equal("#/components/schemas/Order", schema.GetProperty("$ref").GetString());
    }

    /// <summary>
    /// And what it answers with. The return type reached neither the 200 nor
    /// <c>components/schemas</c>, so every registered operation published a bare
    /// <c>"200": {"description": "OK"}</c> - exactly the half a client cannot be generated from.
    /// </summary>
    [ModuleTest]
    public async Task ALambdaPublishesWhatItAnswersWith(ITestWebApp app)
    {
        var paths = await Paths(app);

        var schema = paths
            .GetProperty("/registered/acme/ping/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        Assert.Equal("#/components/schemas/Order", schema.GetProperty("$ref").GetString());
    }

    /// <summary>
    /// A registered operation documents under the class the registration is written in, which is
    /// the group a reader is looking for. The tag came from the handler's controller type, and a
    /// lambda's is its delegate type - so every one of them documented under <c>Func</c>.
    /// </summary>
    [ModuleTest]
    public async Task ALambdaDocumentsUnderTheClassThatRegisteredIt(ITestWebApp app)
    {
        var paths = await Paths(app);

        var tags = paths
            .GetProperty("/registered/acme/ping/{id}")
            .GetProperty("get")
            .GetProperty("tags");

        Assert.Equal("Tenant", tags[0].GetString());
    }

    /// <summary>
    /// A constrained token on a lambda route publishes the 404 it answers and not the 400 it
    /// cannot.
    /// </summary>
    /// <remarks>
    /// The routing guide's rule is about the path the handler is served at, and a lambda had a
    /// placeholder there - so every registered operation published the converter's 400 and not the
    /// router's 404, the reverse of what the same handler publishes when it is declared with an
    /// attribute. The build now states the constraint the token needs and the registry refuses a
    /// registration without it, so the operation is true of every path the route is served at.
    /// </remarks>
    [ModuleTest]
    public async Task ALambdaWithAConstrainedTokenPublishesThe404AndNotThe400(ITestWebApp app)
    {
        var paths = await Paths(app);

        var responses = paths
            .GetProperty("/registered/acme/ping/{id}")
            .GetProperty("get")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("404", out _));
        Assert.False(responses.TryGetProperty("400", out _));
    }

    /// <summary>
    /// And the wire agrees. This is the answer the document used not to describe.
    /// </summary>
    [ModuleTest]
    public async Task AValueThatFailsTheConstraintAnswers404(ITestWebApp app)
    {
        var response = await app.Get("/registered/acme/ping/abc");

        Assert.Equal(404, response.StatusCode);
    }

    /// <remarks>
    /// A registered route's body model has to be in <c>components</c>, or its operation carries a
    /// <c>$ref</c> to nothing.
    /// </remarks>
    [ModuleTest]
    public async Task ARegisteredBodyResolvesToAComponent(ITestWebApp app)
    {
        var response = await app.Get("/openapi.json");

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Order", out _));
    }

    /// <summary>
    /// A registered lambda's declared media types, in the operation the registry splices.
    /// </summary>
    /// <remarks>
    /// The declaration reached the handler's metadata and nothing else, so every registered
    /// operation published a bare <c>application/json</c> whatever the lambda said it answers
    /// with - and a client generated from the document sent an <c>Accept</c> the route does not
    /// produce.
    /// </remarks>
    [ModuleTest]
    public async Task ARegisteredLambdaPublishesTheMediaTypeItDeclares(ITestWebApp app)
    {
        var paths = await Paths(app);

        var content = paths
            .GetProperty("/registered/acme/label/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");

        Assert.True(content.TryGetProperty("text/plain", out _));
        Assert.False(content.TryGetProperty("application/json", out _));
    }

    /// <remarks>
    /// An operation whose response a view writes publishes what the view writes, not the model it
    /// was handed - the same rule the declared form follows.
    /// </remarks>
    [ModuleTest]
    public async Task ARegisteredLambdaNamingAViewPublishesWhatTheViewWrites(ITestWebApp app)
    {
        var paths = await Paths(app);

        var content = paths
            .GetProperty("/registered/acme/card/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");

        Assert.True(content.TryGetProperty("text/html", out var html));
        Assert.Equal("string", html.GetProperty("schema").GetProperty("type").GetString());
    }
}
