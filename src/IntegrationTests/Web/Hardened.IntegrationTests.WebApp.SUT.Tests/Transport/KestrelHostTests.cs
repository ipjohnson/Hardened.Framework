using System.Text.Json;
using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Kestrel.Testing;
using Microsoft.Kiota.Abstractions;
using NSubstitute;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The harness on a real socket: <c>[KestrelHost]</c> on the class runs the application on
/// Kestrel over each test's own container, on a loopback port the kernel picks, and everything
/// the test holds sends there.
/// </summary>
/// <remarks>
/// Each shape here is one the pipeline tests assert in-process: <c>ITestWebApp</c>, a mock behind
/// a route, a generated client and a Refit interface read by <c>Returns&lt;T&gt;()</c>,
/// <c>LastResponse</c>, and the credential attributes. What the socket adds is what the wire
/// adds - Kestrel's framing and headers, a compressed body arriving compressed - and what it
/// takes away is the exception a handler threw, which <see cref="TestWebResponse.Failure"/>
/// reports only in-process; the two tests at the end hold both halves.
/// </remarks>
[KestrelHost]
public class KestrelHostTests {

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [HardenedTest]
    public async Task ARequestAnswersWithWhatKestrelWrote(ITestWebApp app) {
        var response = await app.Get("/verbs/item/42");

        response.Assert.Ok();
        Assert.Equal("got:42", response.Deserialize<string>());
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
        Assert.True(
            response.Headers.ContainsKey("Content-Length") || response.Headers.ContainsKey("Transfer-Encoding"),
            "the framing Kestrel chose");
        Assert.Null(response.Failure);
    }

    /// <summary>
    /// The harness asks for gzip, and over a socket the body arrives as Kestrel sent it: encoded,
    /// with the coding named, and <c>Deserialize</c> undoes it the way it does in-process.
    /// </summary>
    [HardenedTest]
    public async Task ACompressedBodyArrivesEncodedAndReadsDecoded(ITestWebApp app) {
        var response = await app.Get("/compression/readings");

        response.Assert.Ok();
        Assert.Equal("gzip", response.Headers["Content-Encoding"].ToString());
        Assert.Equal(20, response.Deserialize<JsonElement>().GetArrayLength());
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

        Assert.Equal(3, created.Value.Values!.Count);
        Assert.Equal("/verbs/item/3", created.Location);
    }

    [HardenedTest]
    public async Task ARefitInterfaceSendsToTheSocketAndReturnsReadsIt(IWebAppApi api) {
        var created = await api.CreateLocated(new MathAddModel { Values = [1, 2, 3] })
            .Returns<Created<MathAddModel>>();

        Assert.Equal("/verbs/item/3", created.Location);
    }

    [HardenedTest]
    public async Task LastResponseIsWhatCameBackOverTheWire(WebAppClient client) {
        await client.Verbs.Emptied.DeleteAsync(cancellationToken: Token);

        Assert.Equal(204, LastResponse.Status);
        Assert.True(LastResponse.Headers.ContainsKey("Date"));
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

    [HardenedTest]
    public async Task AnUnmatchedPathIs404(ITestWebApp app) {
        var response = await app.Get("/no/such/route");

        response.Assert.NotFound();
    }

    /// <summary>
    /// The half a socket takes away: the handler's exception does not cross the wire, so the
    /// envelope is all there is.
    /// </summary>
    [HardenedTest]
    public async Task OverTheSocketAHandlersExceptionDoesNotCross(ITestWebApp app) {
        var response = await app.Get("/errors/server");

        Assert.Equal(500, response.StatusCode);
        Assert.Null(response.Failure);
    }

    /// <summary>
    /// A method opts back to the pipeline inside a socket class, and gets the half only the
    /// pipeline has.
    /// </summary>
    [HardenedTest]
    [PipelineHost]
    public async Task InProcessTheExceptionIsReported(ITestWebApp app) {
        var response = await app.Get("/errors/server");

        Assert.Equal(500, response.StatusCode);
        Assert.IsType<InvalidOperationException>(response.Failure);
    }
}
