using System.Net;
using Hardened.Requests.Testing;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// Credentials as attributes, through the real pipeline: the attribute in scope reaches a guarded
/// handler through <c>app.Get</c> and through a client, and nothing in scope refuses it.
/// </summary>
/// <remarks>
/// The class carries a grant so the method-level and parameter-level cases have something to
/// override. <see cref="AnonymousCredentialTests"/> next door carries none, and the two classes
/// run in parallel: a header leaking from one to the other would fail both.
/// </remarks>
[Grants("pets:read")]
public class CredentialTests {

    [HardenedTest]
    public async Task TheClassGrantReachesAGuardedHandlerThroughTheHarness(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task TheClassGrantReachesAGuardedHandlerThroughAClient(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [HardenedTest]
    public async Task TheClassGrantReachesAGuardedHandlerThroughAnHttpClient(ITestWebApp app) {
        using var client = app.CreateHttpClient();
        using var response = await client.GetAsync("/authorization/pets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The method's grant replaces the class's, so the class's route is refused.</summary>
    [HardenedTest]
    [Grants("pets:write")]
    public async Task TheMethodGrantBeatsTheClassGrant(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        response.Assert.Forbidden();
    }

    [HardenedTest]
    [Anonymous]
    public async Task AnonymousOnTheMethodCancelsTheClassGrant(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        response.Assert.Unauthorized();
    }

    /// <summary>
    /// Two parameters of one client type with two credentials: two instances, two answers.
    /// </summary>
    [HardenedTest]
    public async Task TwoParametersCarryTwoCredentials(
        ProbeClient reader, [Anonymous] ProbeClient nobody, [Grants("pets:write")] ProbeClient writer) {
        using var read = await reader.Pets(TestContext.Current.CancellationToken);
        using var refused = await nobody.Pets(TestContext.Current.CancellationToken);
        using var forbidden = await writer.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.NotSame(reader, nobody);
    }

    /// <summary>A grant on the harness parameter itself, for the one request that needs it.</summary>
    [HardenedTest]
    public async Task AGrantOnTheHarnessParameterAppliesToItsRequests([Grants("pets:read", "pets:write")] ITestWebApp manager) {
        var response = await manager.Get("/authorization/pets-manage");

        response.Assert.Ok();
    }

    /// <summary>A header the test set in the configure callback is the test's, not the attribute's.</summary>
    [HardenedTest]
    public async Task AHeaderSetByTheTestBeatsTheAttribute(ITestWebApp app) {
        var response = await app.Get(
            "/authorization/pets", request => request.Headers[TestGrantsPrincipalSource.GrantsHeader] = "-");

        response.Assert.Forbidden();
    }

    /// <summary>The credential decided inside the test, on a client built inside it.</summary>
    [HardenedTest]
    public async Task ACredentialComputedInTheTestBuildsAClientWithIt(ITestWebApp app) {
        var writer = app.CreateClient<ProbeClient>(new TestCredential(new[] { "pets:read", "pets:write" }));

        using var response = await writer.Http.GetAsync("/authorization/pets-manage", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// No attribute anywhere in scope: the application authenticates its own way, which here is the
/// same source reading a header the test sets by hand, and a request with no header is anonymous.
/// </summary>
public class AnonymousCredentialTests {

    [HardenedTest]
    public async Task NoAttributeSendsNoCredential(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        response.Assert.Unauthorized();
    }

    [HardenedTest]
    public async Task TheApplicationsOwnSourceStillReadsAHeaderTheTestSets(ITestWebApp app) {
        var response = await app.Get(
            "/authorization/pets", request => request.Headers[TestGrantsPrincipalSource.GrantsHeader] = "pets:read");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task NoAttributeSendsNoCredentialThroughAClient(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [HardenedTest]
    [Grants("pets:read")]
    public async Task AMethodGrantAppliesWithNoClassGrant(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [HardenedTest]
    [Subject("pia")]
    public async Task ASubjectAloneIsAKnownCallerHoldingNothing(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        // Authenticated, so the refusal is a 403 rather than a 401.
        response.Assert.Forbidden();
    }
}
