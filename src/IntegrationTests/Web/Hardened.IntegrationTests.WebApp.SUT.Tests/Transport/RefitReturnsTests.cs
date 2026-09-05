using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// A Refit client, asserted in the contract's own vocabulary through the same call site the
/// Kiota tests use.
/// </summary>
/// <remarks>
/// <see cref="IWebAppApi"/> is built by the route <c>[assembly: RefitTesting]</c> names in
/// Bootstrap.cs - an interface is a test parameter, with nothing written per client. Every status
/// is one <see cref="KiotaReturnsTests"/> asserts through the other generator, which is the
/// point: one vocabulary, two clients.
/// </remarks>
public class RefitReturnsTests {

    [HardenedTest]
    public async Task ACreatedResponseCarriesItsBodyAndItsLocation(IWebAppApi api) {
        var created = await api.CreateLocated(new MathAddModel { Values = [1, 2, 3] })
            .Returns<Created<MathAddModel>>();

        Assert.Equal([1, 2, 3], created.Value.Values);
        Assert.Equal("/verbs/item/3", created.Location);
    }

    [HardenedTest]
    public async Task TheOtherCaseOfTheSetAnswersItsStatus(IWebAppApi api) {
        await api.CreateLocated(new MathAddModel { Values = [] }).ReturnsStatus<BadRequest>();
    }

    [HardenedTest]
    public async Task ADeclared204IsNoContent(IWebAppApi api) {
        await api.Empty().Returns<NoContent>();
    }

    [HardenedTest]
    public async Task ATwoHundredCarriesTheBodyAndEveryHeader(IWebAppApi api) {
        var answer = await api.Add(new MathAddModel { Values = [1, 2, 3] }).Returns<Ok<int>>();

        Assert.Equal(6, answer.Value);
        Assert.StartsWith("application/json", answer.Headers!["Content-Type"]);
    }

    /// <summary>
    /// Refit has no error mapping, so the 422's body is read as the expectation's type argument
    /// through the client's own serializer - here the framework's own model of the refusal.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredRefusalIsReadThroughTheClientsSerializer(IWebAppApi api) {
        var refused = await api.RegisterDeclaring422(new RegistrationModel { Name = "too young", Age = 5 })
            .Returns<UnprocessableContent<RequestValidationError>>();

        Assert.Contains(refused.Body.Errors, error => error.Field.EndsWith("age", StringComparison.OrdinalIgnoreCase));
    }

    [HardenedTest]
    [Grants("pets:write")]
    public async Task AGuardsRefusalIsTheErrorModelAt403(IWebAppApi api) {
        var forbidden = await api.Pets().Returns<Forbidden<ErrorModel>>();

        Assert.Equal("This request is not permitted.", forbidden.Body.Message);
    }

    [HardenedTest]
    public async Task AnUndeclaredRefusalAnswersItsStatus(IWebAppApi api) {
        await api.Pets().ReturnsStatus<Unauthorized>();
    }

    [HardenedTest]
    public async Task TheWrongExpectationNamesBothStatuses(IWebAppApi api) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.Add(new MathAddModel { Values = [1] }).Returns<NoContent>());

        Assert.Contains("Expected 204 (NoContent)", failure.Message);
        Assert.Contains("answered 200", failure.Message);
    }

    /// <summary>
    /// A method returning the body alone has dropped the status, and the failure says which
    /// Refitter option puts it back.
    /// </summary>
    [HardenedTest]
    public async Task AMethodReturningTheBodyAloneCannotBeAnExpectation(IWebAppApi api) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.GetItem("42").Returns<Ok<string>>());

        Assert.Contains("no route that read this call, which returned a String", failure.Message);
        Assert.Contains("--use-api-response", failure.Message);
    }

    [HardenedTest]
    public async Task TwoClientsInOneTestEachAnswerForTheirOwnCall(
        [Grants("pets:read")] IWebAppApi reader, [Anonymous] IWebAppApi nobody) {
        var pets = await reader.Pets().Returns<Ok<string>>();

        await nobody.Pets().ReturnsStatus<Unauthorized>();

        Assert.Equal("\"pets\"", pets.Value);
    }

    [HardenedTest]
    public async Task AClientCreatedInsideTheTestIsBuiltByTheSameRoute(ITestWebApp app) {
        var api = app.CreateClient<IWebAppApi>(new TestCredential(["pets:read"]));

        var pets = await api.Pets().Returns<Ok<string>>();

        Assert.Equal("\"pets\"", pets.Value);
    }
}
