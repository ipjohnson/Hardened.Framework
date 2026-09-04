using System.Net;
using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using NSubstitute;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// Typed clients as test parameters, built over the pipeline by convention or through a factory.
/// </summary>
public class TypedClientTests {

    [HardenedTest]
    public async Task AClientWithAnHttpClientConstructorIsInjectedWithNoFactory(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("http://harness/", client.Http.BaseAddress!.ToString());
    }

    [HardenedTest]
    public async Task AClientWithAnotherConstructorIsInjectedThroughItsFactory(AdaptedClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [HardenedTest]
    public void AClientWithNeitherRouteFailsNamingBoth(ITestWebApp app) {
        var failure = Assert.Throws<InvalidOperationException>(() => app.CreateClient<OrphanClient>());

        Assert.Contains("ITestClientFactory<OrphanClient>", failure.Message);
        Assert.Contains("exactly one HttpClient", failure.Message);
    }

    /// <summary>
    /// The mock is registered into the same graph the handler resolves from, so a client reaching
    /// the handler sees it, exactly as <c>app.Post</c> does.
    /// </summary>
    [HardenedTest]
    public async Task AMockIsVisibleToAHandlerReachedThroughAClient(
        ProbeClient client, [Mock] IMathService<int> mathService) {
        mathService.Add(Arg.Any<int[]>()).Returns(100);

        var sum = await client.Add(new MathAddModel { Values = new List<int> { 1, 2 } }, TestContext.Current.CancellationToken);

        Assert.Equal(100, sum);
    }

    [HardenedTest]
    public async Task TheHarnessAndAClientDriveOnePipeline(ITestWebApp app, ProbeClient client) {
        var direct = await app.Post(new MathAddModel { Values = new List<int> { 10, 20, 30 } }, "/int/add");
        var viaClient = await client.Add(new MathAddModel { Values = new List<int> { 10, 20, 30 } }, TestContext.Current.CancellationToken);

        direct.Assert.Ok();

        Assert.Equal(direct.Deserialize<int>(), viaClient);
    }
}
