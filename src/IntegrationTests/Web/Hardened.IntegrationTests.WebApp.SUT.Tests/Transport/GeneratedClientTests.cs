using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Microsoft.Kiota.Abstractions;
using NSubstitute;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The Kiota-generated client, driven through the real pipeline with no socket.
/// </summary>
/// <remarks>
/// <see cref="WebAppClient"/> is generated at build time, by Hardened.IntegrationTests.WebApp.SUT.Client,
/// from openapi/Application.json - the file <c>ExportedDocumentTests</c> holds byte-identical to
/// the document the application serves. So these tests hold the whole chain: the document the
/// export writes is one a generator reads, the members it generates are the routes the application
/// serves, and bodies, statuses and refusals round-trip through the transport. The client is a test
/// parameter; <see cref="WebAppClientFactory"/> in TestClients.cs says how it is built. What the
/// client does not surface - the status it did not throw on - is read from <see cref="LastResponse"/>.
/// </remarks>
public class GeneratedClientTests {

    [HardenedTest]
    public async Task APathParameterReachesTheHandler(WebAppClient client) {
        var answer = await client.Verbs.Item["42"].GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("got:42", answer);
    }

    [HardenedTest]
    public async Task ABodyIsSerializedAndTheAnswerDeserialized(WebAppClient client) {
        var sum = await client.Int.Add.PostAsync(
            new ClientModels.MathAddModel { Values = [1, 2, 3] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, sum);
    }

    /// <summary>One path, three verbs, and a query parameter on the fourth: each reaches its own handler.</summary>
    [HardenedTest]
    public async Task EachVerbReachesItsOwnHandler(WebAppClient client) {
        var token = TestContext.Current.CancellationToken;

        var deleted = await client.Verbs.Item["7"].DeleteAsync(cancellationToken: token);
        var patched = await client.Verbs.Item["7"].PatchAsync(new ClientModels.MathAddModel { Values = [1, 2] }, cancellationToken: token);
        var byQuery = await client.Verbs.Item.DeleteAsync(request => request.QueryParameters.Name = "stale", token);

        Assert.Equal("deleted:7", deleted);
        Assert.Equal("patched:7:1,2", patched);
        Assert.Equal("deleted:stale", byQuery);
    }

    /// <summary>
    /// The client does not surface a success status; the transport keeps it for the test.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredStatusIsReadFromLastResponse(WebAppClient client) {
        var created = await client.Verbs.Created.PostAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("created", created);
        Assert.Equal(201, LastResponse.Status);

        await client.Verbs.Emptied.DeleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(204, LastResponse.Status);
    }

    [HardenedTest]
    public async Task AnEnumComesBackAsTheGeneratedMember(WebAppClient client) {
        var ticket = await client.EnumVocabulary.Ticket.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Ship it", ticket!.Title);
        Assert.Equal(ClientModels.Ticket_priority.InProgress, ticket.Priority);
    }

    /// <summary>
    /// [Throws&lt;RequestValidationError&gt;(422)] on the handler put the 422 and its body in the
    /// document, so Kiota generated a typed exception for it, and the refusal arrives as that type
    /// carrying the errors the filter wrote.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredRefusalIsTheTypedExceptionTheDocumentPromised(WebAppClient client) {
        var refusal = await Assert.ThrowsAsync<ClientModels.RequestValidationError>(() =>
            client.Registration.Declared422.PostAsync(
                new ClientModels.RegistrationModel { Name = "too young", Age = 5 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(422, refusal.ResponseStatusCode);
        Assert.Contains(refusal.Errors!, error => error.Field!.EndsWith("age", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The undeclared route answers the same body as a 400; the document says so, and the type follows.</summary>
    [HardenedTest]
    public async Task TheDefaultRefusalIsTypedToo(WebAppClient client) {
        var refusal = await Assert.ThrowsAsync<ClientModels.RequestValidationError>(() =>
            client.Registration.PostAsync(
                new ClientModels.RegistrationModel { Name = "too young", Age = 5 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(400, refusal.ResponseStatusCode);
    }

    /// <summary>
    /// A refusal the document does not declare has no type to be: Kiota's base exception, with the
    /// status on it.
    /// </summary>
    [HardenedTest]
    public async Task AnUndeclaredRefusalIsABareApiException(WebAppClient client) {
        var refusal = await Assert.ThrowsAsync<ApiException>(() =>
            client.Authorization.Pets.GetAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(401, refusal.ResponseStatusCode);
    }

    [HardenedTest]
    [Grants("pets:read")]
    public async Task TheCredentialInScopeReachesAGuardedHandler(WebAppClient client) {
        var pets = await client.Authorization.Pets.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(pets);
        Assert.Equal(200, LastResponse.Status);
    }

    /// <summary>Three parameters of the generated type with three credentials: three instances, three answers.</summary>
    [HardenedTest]
    public async Task ThreeGeneratedClientsCarryThreeCredentials(
        [Grants("pets:read")] WebAppClient reader, [Anonymous] WebAppClient nobody, [Grants("pets:write")] WebAppClient writer) {
        var token = TestContext.Current.CancellationToken;

        var pets = await reader.Authorization.Pets.GetAsync(cancellationToken: token);
        var refused = await Assert.ThrowsAsync<ApiException>(() => nobody.Authorization.Pets.GetAsync(cancellationToken: token));
        var forbidden = await Assert.ThrowsAsync<ApiException>(() => writer.Authorization.Pets.GetAsync(cancellationToken: token));

        Assert.NotNull(pets);
        Assert.Equal(401, refused.ResponseStatusCode);
        Assert.Equal(403, forbidden.ResponseStatusCode);
        Assert.NotSame(reader, nobody);
    }

    /// <summary>
    /// The mock is registered into the graph the handler resolves from, so the generated client
    /// reaches it exactly as <c>app.Post</c> does.
    /// </summary>
    [HardenedTest]
    public async Task AMockIsVisibleToAHandlerReachedThroughTheGeneratedClient(
        WebAppClient client, [Mock] IMathService<int> mathService) {
        mathService.Add(Arg.Any<int[]>()).Returns(100);

        var sum = await client.Int.Add.PostAsync(
            new ClientModels.MathAddModel { Values = [1, 2] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(100, sum);
    }

    [HardenedTest]
    public async Task TheHarnessAndTheGeneratedClientDriveOnePipeline(ITestWebApp app, WebAppClient client) {
        var direct = await app.Post(new MathAddModel { Values = new List<int> { 10, 20, 30 } }, "/int/add");
        var viaClient = await client.Int.Add.PostAsync(
            new ClientModels.MathAddModel { Values = [10, 20, 30] },
            cancellationToken: TestContext.Current.CancellationToken);

        direct.Assert.Ok();

        Assert.Equal(direct.Deserialize<int>(), viaClient);
    }
}
