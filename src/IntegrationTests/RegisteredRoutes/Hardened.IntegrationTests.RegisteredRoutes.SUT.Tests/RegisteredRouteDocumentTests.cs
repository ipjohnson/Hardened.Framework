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

    [HardenedTest]
    public async Task TheDeclaredRoutesAreStillThere(ITestWebApp app)
    {
        var paths = await Paths(app);

        Assert.True(paths.TryGetProperty("/registered/orders/{id}", out _));
    }

    /// <remarks>
    /// One entry per tenant, from a list that did not exist when the document was written.
    /// </remarks>
    [HardenedTest]
    public async Task ARegisteredControllerRouteIsDescribed(ITestWebApp app)
    {
        var paths = await Paths(app);

        Assert.True(paths.TryGetProperty("/registered/acme/orders/{id}", out var acme));
        Assert.True(acme.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/registered/globex/orders/{id}", out _));
    }

    [HardenedTest]
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
    [HardenedTest]
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
    [HardenedTest]
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
    [HardenedTest]
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

    /// <remarks>
    /// A registered route's body model has to be in <c>components</c>, or its operation carries a
    /// <c>$ref</c> to nothing.
    /// </remarks>
    [HardenedTest]
    public async Task ARegisteredBodyResolvesToAComponent(ITestWebApp app)
    {
        var response = await app.Get("/openapi.json");

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Order", out _));
    }
}
