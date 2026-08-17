using Hardened.IntegrationTests.WebApp.SUT.Filters;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Authorization driven through the real pipeline.
///
/// <para>
/// Unit tests cover the filter, the fold and the challenge in isolation. What these confirm is the
/// wiring nothing else touches: that an attribute written on a handler reaches handler metadata at
/// all, that the startup service installs the provider, that the provider's filter lands in the
/// chain, and that a refusal becomes a status and a header on a real response.
/// </para>
/// </summary>
public class AuthorizationTests {

    private static Action<TestWebRequest> Holding(string grants) =>
        request => request.Headers[TestPrincipalMiddleware.GrantsHeader] = grants;

    #region public routes

    /// <summary>
    /// The default posture. Nothing has opted in, so a handler carrying no attribute is reachable
    /// exactly as it was before any of this existed.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerWithNoAttributeIsStillPublic(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/unguarded");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task AllowAnonymousIsReachableWithoutACredential(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/open");

        response.Assert.Ok();
    }

    #endregion

    #region refusals

    /// <summary>
    /// No credential at all is a 401, not a 403: the caller has not failed a permission check, it
    /// has not identified itself.
    /// </summary>
    [HardenedTest]
    public async Task AGuardedRouteRefusesAnAnonymousCallerWith401(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/pets");

        response.Assert.Unauthorized();
    }

    /// <summary>
    /// RFC 6750 asks for the challenge, and it is the whole reason a status alone was not enough -
    /// it tells the client how to authenticate rather than only that it must.
    /// </summary>
    [HardenedTest]
    public async Task ARefusalCarriesAChallengeHeader(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/pets");

        Assert.True(response.Headers.TryGetValue("WWW-Authenticate", out var challenge));
        Assert.Contains("Bearer", challenge.ToString());
    }

    /// <summary>
    /// Authenticated but short of grants is a 403, and the challenge names what would have worked.
    /// </summary>
    [HardenedTest]
    public async Task AnAuthenticatedCallerShortOfGrantsIsForbidden(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/pets-manage", Holding("pets:read"));

        response.Assert.Forbidden();

        Assert.True(response.Headers.TryGetValue("WWW-Authenticate", out var challenge));
        Assert.Contains("insufficient_scope", challenge.ToString());
        Assert.Contains("pets:write", challenge.ToString());
    }

    #endregion

    #region admitted

    [HardenedTest]
    public async Task ACallerHoldingTheGrantIsAdmitted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/authorization/pets", Holding("pets:read"));

        response.Assert.Ok();
    }

    /// <summary>
    /// Grants within one attribute are an and, so holding both admits the request.
    /// </summary>
    [HardenedTest]
    public async Task ACallerHoldingBothGrantsIsAdmitted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/authorization/pets-manage", Holding("pets:read pets:write"));

        response.Assert.Ok();
    }

    /// <summary>
    /// Stacked attributes conjoin, so the caller needs everything all of them named.
    /// </summary>
    [HardenedTest]
    public async Task StackedAttributesAdmitOnlyTheCallerHoldingEverything(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/authorization/stacked", Holding("pets:read admin:*"));

        response.Assert.Ok();
    }

    /// <summary>
    /// Holding what one of them named is not enough, which is the whole point: writing the second
    /// attribute restricted the route rather than opening it.
    /// </summary>
    [HardenedTest]
    public async Task StackedAttributesRefuseACallerHoldingOnlyOne(ITestWebApp testWebApp) {
        var viaPets = await testWebApp.Get("/authorization/stacked", Holding("pets:read"));
        var viaAdmin = await testWebApp.Get("/authorization/stacked", Holding("admin:*"));

        viaPets.Assert.Forbidden();
        viaAdmin.Assert.Forbidden();
    }

    /// <summary>
    /// An application's own attribute, deriving from <c>[AuthorizeGrants]</c>, guards its route.
    /// </summary>
    [HardenedTest]
    public async Task ADerivedAttributeGuardsItsRoute(ITestWebApp testWebApp) {
        var holding = await testWebApp.Get(
            "/authorization/derived", Holding("pets:read pets:write"));
        var lacking = await testWebApp.Get("/authorization/derived", Holding("pets:read"));

        holding.Assert.Ok();
        lacking.Assert.Forbidden();
    }

    #endregion
}
