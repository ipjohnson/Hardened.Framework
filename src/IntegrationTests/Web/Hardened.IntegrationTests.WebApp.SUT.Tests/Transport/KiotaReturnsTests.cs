using Hardened.IntegrationTests.WebApp.SUT.Client;
using Hardened.Requests.Abstract.Responses;
using ClientModels = Hardened.IntegrationTests.WebApp.SUT.Client.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The Kiota-generated client, asserted in the contract's own vocabulary: the response type is the
/// assertion.
/// </summary>
/// <remarks>
/// The client is built by the route <c>[assembly: KiotaTesting]</c> names in Bootstrap.cs - no
/// factory in this project - which is what puts the recording handler in its chain that a
/// success's status and headers are read from. Every status here is also asserted the long way in
/// <see cref="GeneratedClientTests"/>, so a disagreement between the two is a defect in the
/// package rather than in the application.
/// </remarks>
public class KiotaReturnsTests {

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The case that needs the recording: the client returns the body and drops the 201 and its
    /// Location, and both come back on the response type.
    /// </summary>
    [HardenedTest]
    public async Task ACreatedResponseCarriesItsBodyAndItsLocation(WebAppClient client) {
        var created = await client.Verbs.Located
            .PostAsync(new ClientModels.MathAddModel { Values = [1, 2, 3] }, cancellationToken: Token)
            .Returns<Created<ClientModels.MathAddModel>>();

        Assert.Equal(3, created.Value.Values!.Count);
        Assert.Equal("/verbs/item/3", created.Location);
    }

    /// <summary>
    /// The other case of the same set. Its body is the framework's own problem shape, which the
    /// document declares and Kiota types; the status alone is what this asserts.
    /// </summary>
    [HardenedTest]
    public async Task TheOtherCaseOfTheSetAnswersItsStatus(WebAppClient client) {
        await client.Verbs.Located
            .PostAsync(new ClientModels.MathAddModel { Values = [] }, cancellationToken: Token)
            .ReturnsStatus<BadRequest>();
    }

    [HardenedTest]
    public async Task ADeclared204IsNoContent(WebAppClient client) {
        await client.Verbs.Emptied.DeleteAsync(cancellationToken: Token).Returns<NoContent>();
    }

    [HardenedTest]
    public async Task ATwoHundredCarriesTheBodyAndEveryHeader(WebAppClient client) {
        var answer = await client.Verbs.Item["42"].GetAsync(cancellationToken: Token).Returns<Ok<string>>();

        Assert.Equal("got:42", answer.Value);
        Assert.StartsWith("application/json", answer.Headers!["Content-Type"]);
    }

    /// <summary>A declared refusal is the model Kiota threw, at the status the case declares.</summary>
    [HardenedTest]
    public async Task ADeclaredRefusalIsTheTypedModelAtItsStatus(WebAppClient client) {
        var refused = await client.Registration.Declared422
            .PostAsync(new ClientModels.RegistrationModel { Name = "too young", Age = 5 }, cancellationToken: Token)
            .Returns<UnprocessableContent<ClientModels.RequestValidationError>>();

        Assert.Contains(refused.Body.Errors!, error => error.Field!.EndsWith("age", StringComparison.OrdinalIgnoreCase));
    }

    [HardenedTest]
    [Grants("pets:write")]
    public async Task AGuardsRefusalIsTheErrorModelAt403(WebAppClient client) {
        var forbidden = await client.Authorization.Pets.GetAsync(cancellationToken: Token)
            .Returns<Forbidden<ClientModels.ErrorModel>>();

        Assert.Equal("This request is not permitted.", forbidden.Body.Message);
    }

    /// <summary>An undeclared refusal has no body type to be, so only its status can be named.</summary>
    [HardenedTest]
    public async Task AnUndeclaredRefusalAnswersItsStatus(WebAppClient client) {
        await client.Authorization.Pets.GetAsync(cancellationToken: Token).ReturnsStatus<Unauthorized>();
    }

    /// <summary>The wrong expectation fails naming both statuses in the contract's words.</summary>
    [HardenedTest]
    public async Task TheWrongExpectationNamesBothStatuses(WebAppClient client) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Verbs.Item["1"].GetAsync(cancellationToken: Token).Returns<NoContent>());

        Assert.Contains("Expected 204 (NoContent)", failure.Message);
        Assert.Contains("answered 200", failure.Message);
    }

    /// <summary>Two clients with two credentials in one test, each asserted on its own answer.</summary>
    [HardenedTest]
    public async Task TwoClientsInOneTestEachAnswerForTheirOwnCall(
        [Grants("pets:read")] WebAppClient reader, [Anonymous] WebAppClient nobody) {
        var pets = await reader.Authorization.Pets.GetAsync(cancellationToken: Token).Returns<Ok<string>>();

        await nobody.Authorization.Pets.GetAsync(cancellationToken: Token).ReturnsStatus<Unauthorized>();

        Assert.Equal("pets", pets.Value);
    }

    /// <summary>
    /// The harness's other door still works beside the route: a client asked for inside the test
    /// is the same construction as a parameter.
    /// </summary>
    [HardenedTest]
    public async Task AClientCreatedInsideTheTestIsBuiltByTheSameRoute(ITestWebApp app) {
        var client = app.CreateClient<WebAppClient>(new TestCredential(["pets:read"]));

        var pets = await client.Authorization.Pets.GetAsync(cancellationToken: Token).Returns<Ok<string>>();

        Assert.Equal("pets", pets.Value);
    }
}
