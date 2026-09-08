using Hardened.IntegrationTests.AzureHttp.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.AzureHttp.SUT.Tests;

/// <summary>
/// A web application deployed as a function: ordinary verbs on a controller, reached through an
/// HTTP trigger.
///
/// <para>
/// Nothing here mentions Azure. The request data, the invocation handler and the response data
/// are all behind <see cref="ITestWebApp"/>, and which host builds them is one attribute in
/// Bootstrap.cs - so this file is what a Kestrel suite would look like, which is the point. It is
/// the API Gateway fixture's test file, unchanged.
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
    /// The 404 an unmatched path has to be. The response's status is null until something sets
    /// it, so the not-found handler can tell an unmatched route from an answered one.
    /// </summary>
    [HardenedTest]
    public async Task AnUnmatchedPathIsA404(ITestWebApp app) {
        var response = await app.Get("/nothing-here");

        Assert.Equal(404, response.StatusCode);
    }
}
