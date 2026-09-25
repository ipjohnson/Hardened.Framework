using DependencyModules.xUnit.Attributes;
using Hardened.IntegrationTests.CloudRunWeb.SUT;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunWeb.SUT.Tests;

/// <summary>
/// A web application deployed on Cloud Run: ordinary verbs on a controller. The same tests the
/// API Gateway fixture holds; nothing here mentions Google.
/// </summary>
public class HttpServiceTests
{
    [ModuleTest]
    public async Task AGetReachesItsHandlerWithThePathToken(ITestWebApp app)
    {
        var response = await app.Get("/orders/o-1");

        Assert.Equal(200, response.StatusCode);

        var order = response.Deserialize<Order>();

        Assert.Equal("o-1", order.Id);
        Assert.Equal(7, order.Quantity);
    }

    [ModuleTest]
    public async Task APostBindsItsBody(ITestWebApp app)
    {
        var response = await app.Post(new Order { Id = "o-2", Quantity = 3 }, "/orders");

        var order = response.Deserialize<Order>();

        Assert.Equal("o-2", order.Id);
        Assert.Equal(3, order.Quantity);
    }

    [ModuleTest]
    public async Task ADeleteOnTheSamePathReachesADifferentHandler(ITestWebApp app)
    {
        var response = await app.Delete("/orders/o-1");

        Assert.Equal(200, response.StatusCode);
    }

    [ModuleTest]
    public async Task AnUnmatchedPathIsA404(ITestWebApp app)
    {
        var response = await app.Get("/nothing-here");

        Assert.Equal(404, response.StatusCode);
    }

    /// <summary>A JSON POST to a web route is served as a web route; the front door has no envelope to unwrap it into.</summary>
    [ModuleTest]
    public async Task AJsonPostToAWebRouteIsNotMistakenForAnEnvelope(ITestWebApp app)
    {
        var response = await app.Post(new Order { Id = "o-3", Quantity = 1 }, "/orders");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("o-3", response.Deserialize<Order>().Id);
    }
}

/// <summary>The same routes over a Kestrel socket, which is what Cloud Run serves.</summary>
[KestrelRuntime]
public class HttpServiceOverASocketTests
{
    [ModuleTest]
    public async Task AGetReachesItsHandlerWithThePathToken(ITestWebApp app)
    {
        var response = await app.Get("/orders/o-1");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("o-1", response.Deserialize<Order>().Id);
    }

    [ModuleTest]
    public async Task APostBindsItsBody(ITestWebApp app)
    {
        var response = await app.Post(new Order { Id = "o-2", Quantity = 3 }, "/orders");

        Assert.Equal(3, response.Deserialize<Order>().Quantity);
    }

    [ModuleTest]
    public async Task AnUnmatchedPathIsA404(ITestWebApp app)
    {
        var response = await app.Get("/nothing-here");

        Assert.Equal(404, response.StatusCode);
    }
}
