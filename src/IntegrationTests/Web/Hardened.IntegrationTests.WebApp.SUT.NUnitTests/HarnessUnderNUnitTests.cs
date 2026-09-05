using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Kiota.Abstractions;
using NSubstitute;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.NUnitTests;

/// <summary>
/// The web harness, read through NUnit. Each test here has a twin in the xUnit project next door
/// asserting the same answer; the difference is the runner, and that is the point.
/// </summary>
public class HarnessUnderNUnitTests {

    private static CancellationToken Token => TestContext.CurrentContext.CancellationToken;

    [HardenedTest]
    public async Task ARequestThroughTheHarnessAnswers(ITestWebApp app) {
        var response = await app.Get("/verbs/item/1");

        response.Assert.Ok();
        Assert.That(response.Deserialize<string>(), Is.EqualTo("got:1"));
    }

    [HardenedTest]
    public async Task AMockBehindARouteIsTheOneTheHandlerSees(ITestWebApp app, [Mock] IMathService<int> math) {
        math.Add(Arg.Any<int[]>()).Returns(100);

        var response = await app.Post(new MathAddModel { Values = [1, 2, 3] }, "/int/add");

        response.Assert.Ok();
        Assert.That(response.Deserialize<int>(), Is.EqualTo(100));
    }

    [HardenedTest]
    public async Task AGeneratedClientIsAParameterAndReturnsReadsIt(WebAppClient client) {
        var created = await client.Verbs.Located
            .PostAsync(new ClientModels.MathAddModel { Values = [1, 2, 3] }, cancellationToken: Token)
            .Returns<Created<ClientModels.MathAddModel>>();

        Assert.That(created.Value.Values, Has.Count.EqualTo(3));
        Assert.That(created.Location, Is.EqualTo("/verbs/item/3"));
    }

    [HardenedTest]
    public async Task ARefitInterfaceIsAParameterAndReturnsReadsIt(IWebAppApi api) {
        var created = await api.CreateLocated(new MathAddModel { Values = [1, 2, 3] })
            .Returns<Created<MathAddModel>>();

        Assert.That(created.Location, Is.EqualTo("/verbs/item/3"));
    }

    [HardenedTest]
    public async Task LastResponseIsKeyedOnTheRunningTest(WebAppClient client) {
        await client.Verbs.Emptied.DeleteAsync(cancellationToken: Token);

        Assert.That(LastResponse.Status, Is.EqualTo(204));
    }

    [HardenedTest]
    public async Task TwoParametersCarryTwoCredentials(
        [Grants("pets:read")] WebAppClient reader, [Anonymous] WebAppClient nobody) {
        var pets = await reader.Authorization.Pets.GetAsync(cancellationToken: Token);
        var refused = Assert.ThrowsAsync<ApiException>(() => nobody.Authorization.Pets.GetAsync(cancellationToken: Token));

        Assert.That(pets, Is.Not.Null);
        Assert.That(refused!.ResponseStatusCode, Is.EqualTo(401));
    }

    [HardenedTest]
    [Grants("pets:read")]
    public async Task TheCredentialInScopeReachesARefitCall(IWebAppApi api) {
        var pets = await api.Pets().Returns<Ok<string>>();

        Assert.That(pets.Value, Is.EqualTo("\"pets\""));
    }

    /// <summary>A refusal from the harness's own assertion reads as this test's failure, with its message.</summary>
    [HardenedTest]
    public async Task TheHarnessAssertionNamesTheStatus(ITestWebApp app) {
        var response = await app.Get("/no/such/route");

        var failure = Assert.Throws<WebAssertionException>(() => response.Assert.Ok());

        Assert.That(failure!.Message, Is.EqualTo("Expected a 2xx status, the response was 404."));
    }
}
