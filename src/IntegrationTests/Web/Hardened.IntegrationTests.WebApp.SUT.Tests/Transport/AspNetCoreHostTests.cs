using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.Kiota.Abstractions;
using NSubstitute;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The harness inside the real ASP.NET Core pipeline: <c>[AspNetCoreRuntime]</c> on the class -
/// the attribute the application names its host with - builds the application the way
/// <c>Program.cs</c> does, over each test's own container, and everything the test holds sends
/// to its socket.
/// </summary>
/// <remarks>
/// The shapes <see cref="KestrelHostTests"/> asserts on Kestrel alone, plus the one thing this
/// host does differently: it is not terminal, so a path Hardened declares nothing for falls
/// through to ASP.NET's own 404, with no body, where the Kestrel host and the pipeline answer
/// Hardened's.
/// </remarks>
[AspNetCoreRuntime]
public class AspNetCoreHostTests {

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [HardenedTest]
    public async Task ARequestAnswersThroughTheAspNetPipeline(ITestWebApp app) {
        var response = await app.Get("/verbs/item/42");

        response.Assert.Ok();
        Assert.Equal("got:42", response.Deserialize<string>());
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
        Assert.Null(response.Failure);
    }

    [HardenedTest]
    public async Task AMockBehindARouteIsTheOneTheHandlerSees(ITestWebApp app, [Mock] IMathService<int> math) {
        math.Add(Arg.Any<int[]>()).Returns(100);

        var response = await app.Post(new MathAddModel { Values = [1, 2, 3] }, "/int/add");

        response.Assert.Ok();
        Assert.Equal(100, response.Deserialize<int>());
    }

    [HardenedTest]
    public async Task AGeneratedClientSendsToTheSocketAndReturnsReadsIt(WebAppClient client) {
        var created = await client.Verbs.Located
            .PostAsync(new ClientModels.MathAddModel { Values = [1, 2, 3] }, cancellationToken: Token)
            .Returns<Created<ClientModels.MathAddModel>>();

        Assert.Equal("/verbs/item/3", created.Location);
    }

    [HardenedTest]
    public async Task LastResponseIsWhatCameBackOverTheWire(WebAppClient client) {
        await client.Verbs.Emptied.DeleteAsync(cancellationToken: Token);

        Assert.Equal(204, LastResponse.Status);
    }

    [HardenedTest]
    public async Task ThreeParametersCarryThreeCredentialsOverTheWire(
        [Grants("pets:read")] WebAppClient reader, [Anonymous] WebAppClient nobody, [Grants("pets:write")] WebAppClient writer) {
        var pets = await reader.Authorization.Pets.GetAsync(cancellationToken: Token);
        var refused = await Assert.ThrowsAsync<ApiException>(() => nobody.Authorization.Pets.GetAsync(cancellationToken: Token));
        var forbidden = await Assert.ThrowsAsync<ClientModels.ErrorModel>(() => writer.Authorization.Pets.GetAsync(cancellationToken: Token));

        Assert.NotNull(pets);
        Assert.Equal(401, refused.ResponseStatusCode);
        Assert.Equal(403, forbidden.ResponseStatusCode);
    }

    /// <summary>
    /// Not terminal. The application carries [AspNetCoreRuntime], whose not-found handler leaves
    /// the status unset so the request falls through, and nothing is behind Hardened in the
    /// default composition, so ASP.NET's own 404 answers: no envelope, no body.
    /// </summary>
    [HardenedTest]
    public async Task AnUnmatchedPathIsAspNetsOwn404(ITestWebApp app) {
        var response = await app.Get("/no/such/route");

        response.Assert.NotFound();
        Assert.Equal(string.Empty, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task OverTheSocketAHandlersExceptionDoesNotCross(ITestWebApp app) {
        var response = await app.Get("/errors/server");

        Assert.Equal(500, response.StatusCode);
        Assert.Null(response.Failure);
    }

    [HardenedTest]
    [PipelineHost]
    public async Task InProcessTheExceptionIsReported(ITestWebApp app) {
        var response = await app.Get("/errors/server");

        Assert.Equal(500, response.StatusCode);
        Assert.IsType<InvalidOperationException>(response.Failure);
    }
}
