using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.ApiGateway.SUT.Tests;

/// <summary>
/// A web application deployed as a Lambda: ordinary verbs on a controller, reached through an API
/// Gateway payload.
///
/// <para>
/// Nothing here mentions AWS. The proxy event, the invocation loop and the proxy response are all
/// behind <see cref="ITestWebApp"/>, and which host builds them is one attribute in Bootstrap.cs -
/// so this file is what a Kestrel suite would look like, which is the point.
/// </para>
/// </summary>
public class HttpFunctionTests {

    [HardenedTest]
    public async Task AGetReachesItsHandlerWithThePathToken(ITestWebApp app) {
        var response = await app.Get("/orders/o-1");

        Assert.Equal(200, response.StatusCode);

        var order = response.Deserialize<Order>();

        Assert.Equal("o-1", order!.Id);
        Assert.Equal(7, order.Quantity);
    }

    [HardenedTest]
    public async Task APostBindsItsBody(ITestWebApp app) {
        var response = await app.Post(new Order { Id = "o-2", Quantity = 3 }, "/orders");

        var order = response.Deserialize<Order>();

        Assert.Equal("o-2", order!.Id);
        Assert.Equal(3, order.Quantity);
    }

    /// <summary>
    /// The verb is part of the route, so the same path under a different method is a different
    /// handler - and a void one answers without a body.
    /// </summary>
    [HardenedTest]
    public async Task ADeleteOnTheSamePathReachesADifferentHandler(ITestWebApp app) {
        var response = await app.Delete("/orders/o-1");

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>
    /// The 404 this transport spent a long time unable to send. The proxy response's status was a
    /// non-nullable int starting at zero, so the not-found handler never found it unset and every
    /// unmatched path came back as an empty 200.
    /// </summary>
    [HardenedTest]
    public async Task AnUnmatchedPathIsA404(ITestWebApp app) {
        var response = await app.Get("/nothing-here");

        Assert.Equal(404, response.StatusCode);
    }
}
