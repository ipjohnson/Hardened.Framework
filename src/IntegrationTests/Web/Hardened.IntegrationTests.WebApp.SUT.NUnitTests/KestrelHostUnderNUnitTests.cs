using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.NUnitTests;

/// <summary>
/// The socket host under NUnit: the same shapes the xUnit project asserts with
/// <c>[KestrelRuntime]</c>, on the other runner.
/// </summary>
[KestrelRuntime]
public class KestrelHostUnderNUnitTests {

    private static CancellationToken Token => TestContext.CurrentContext.CancellationToken;

    [HardenedTest]
    public async Task ARequestAnswersWithWhatKestrelWrote(ITestWebApp app) {
        var response = await app.Get("/verbs/item/42");

        response.Assert.Ok();
        Assert.That(response.Deserialize<string>(), Is.EqualTo("got:42"));
        Assert.That(response.Headers.ContainsKey("Date"), Is.True, "a header only a server writes");
        Assert.That(response.Failure, Is.Null);
    }

    [HardenedTest]
    public async Task AMockBehindARouteIsTheOneTheHandlerSees(ITestWebApp app, [Mock] IMathService<int> math) {
        math.Add(Arg.Any<int[]>()).Returns(100);

        var response = await app.Post(new MathAddModel { Values = [1, 2, 3] }, "/int/add");

        response.Assert.Ok();
        Assert.That(response.Deserialize<int>(), Is.EqualTo(100));
    }

    [HardenedTest]
    public async Task AGeneratedClientSendsToTheSocketAndReturnsReadsIt(WebAppClient client) {
        var created = await client.Verbs.Located
            .PostAsync(new ClientModels.MathAddModel { Values = [1, 2, 3] }, cancellationToken: Token)
            .Returns<Created<ClientModels.MathAddModel>>();

        Assert.That(created.Location, Is.EqualTo("/verbs/item/3"));
    }

    [HardenedTest]
    public async Task LastResponseIsWhatCameBackOverTheWire(WebAppClient client) {
        await client.Verbs.Emptied.DeleteAsync(cancellationToken: Token);

        Assert.That(LastResponse.Status, Is.EqualTo(204));
        Assert.That(LastResponse.Headers.ContainsKey("Date"), Is.True);
    }

    [HardenedTest]
    [PipelineHost]
    public async Task AMethodOptsBackToThePipeline(ITestWebApp app) {
        var response = await app.Get("/errors/server");

        Assert.That(response.StatusCode, Is.EqualTo(500));
        Assert.That(response.Failure, Is.TypeOf<InvalidOperationException>());
    }
}
