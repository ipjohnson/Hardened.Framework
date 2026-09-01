using Hardened.Requests.Testing;

namespace Hardened.IntegrationTests.Authorization.SUT.Tests;

/// <summary>
/// The default-deny posture, end to end.
///
/// <para>
/// Every link in this chain is unit tested on its own - the attribute, the registration the
/// generator emits, the configuration amender, the filter provider's decision, the filter's refusal.
/// What none of them can show is that the links are connected: that writing
/// <c>[RequireAuthorization]</c> on a module and nothing else is enough to make an unannotated
/// handler refuse a real request over a real host.
/// </para>
/// </summary>
public class DefaultDenyTests {

    private static Action<TestWebRequest> Holding(string grants) =>
        request => request.Headers[TestGrantsPrincipalSource.GrantsHeader] = grants;

    /// <summary>Authenticated, holding nothing.</summary>
    private static Action<TestWebRequest> Authenticated() =>
        Holding(TestGrantsPrincipalSource.AnonymousGrantsValue);

    #region the backstop

    /// <summary>
    /// The case the fixture exists for. The handler declares nothing, no generator emitted a filter
    /// for it specifically, and it still refuses - which is the whole of what "default deny" means.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerThatSaysNothingRefusesAnAnonymousCaller(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/implicit");

        response.Assert.Unauthorized();
    }

    [HardenedTest]
    public async Task TheBackstopRefusalCarriesAChallenge(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/implicit");

        Assert.True(response.Headers.TryGetValue("WWW-Authenticate", out var challenge));
        Assert.Contains("Bearer", challenge.ToString());
    }

    /// <summary>
    /// The backstop asks for authentication and nothing more. A caller who has identified itself
    /// gets through without holding any particular grant, because no handler declared one.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerThatSaysNothingAdmitsAnyAuthenticatedCaller(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/implicit", Authenticated());

        response.Assert.Ok();
    }

    #endregion

    #region opting back out

    [HardenedTest]
    public async Task AllowAnonymousIsReachableWithoutACredential(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/open");

        response.Assert.Ok();
    }

    /// <summary>
    /// Written once on a controller, it covers every route below it - the same reading the build
    /// diagnostic uses, so the two cannot disagree about which handlers are public.
    /// </summary>
    [HardenedTest]
    public async Task AControllerLevelOptOutCoversEveryRouteInIt(ITestWebApp testWebApp) {
        var health = await testWebApp.Get("/public/health");
        var version = await testWebApp.Get("/public/version");

        health.Assert.Ok();
        version.Assert.Ok();
    }

    #endregion

    #region declared requirements still apply

    /// <summary>
    /// The backstop does not replace what a handler declared. A route asking for a grant still asks
    /// for it, and an authenticated caller without it is forbidden rather than admitted by the
    /// weaker default.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredGrantStillAppliesUnderTheBackstop(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/pets", Authenticated());

        response.Assert.Forbidden();
    }

    [HardenedTest]
    public async Task ACallerHoldingTheGrantIsAdmitted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/pets", Holding("pets:read"));

        response.Assert.Ok();
    }

    /// <summary>
    /// No credential is still a 401 rather than a 403, even on a route that declares a grant: the
    /// caller has not failed a permission check, it has not identified itself.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredGrantRefusesAnAnonymousCallerWith401(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/pets");

        response.Assert.Unauthorized();
    }

    [HardenedTest]
    public async Task ShortOfOneGrantIsForbiddenAndSaysWhichOne(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/pets-manage", Holding("pets:read"));

        response.Assert.Forbidden();

        Assert.True(response.Headers.TryGetValue("WWW-Authenticate", out var challenge));
        Assert.Contains("insufficient_scope", challenge.ToString());
        Assert.Contains("pets:write", challenge.ToString());
    }

    [HardenedTest]
    public async Task HoldingBothGrantsIsAdmitted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/guarded/pets-manage", Holding("pets:read pets:write"));

        response.Assert.Ok();
    }

    #endregion
}
