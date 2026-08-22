namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Authorization a description declared, over a real request.
/// </summary>
/// <remarks>
/// <para>
/// Neither handler carries an authorization attribute and this application registers no
/// authorization of its own. Everything guarding these routes came from <c>security</c> in the
/// document, which is the whole claim: a described requirement reaches the pipeline and refuses.
/// </para>
/// <para>
/// <b>No opt-in was needed.</b> <c>AuthorizationFilterProvider</c>'s <c>requireAuthorization</c> flag
/// decides what happens to a handler that declares <em>nothing</em>; a handler that declares
/// something is guarded either way. So a contract that names a scope protects its route on the next
/// build, rather than on the next time somebody remembers to turn a posture on.
/// </para>
/// </remarks>
public class DescribedSecurityTests {

    /// <summary>
    /// A scope in the description refuses a caller who holds nothing.
    /// </summary>
    [HardenedTest]
    public async Task ADescribedScopeRefusesAnAnonymousCaller(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/secured/scoped");

        Assert.Equal(401, response.StatusCode);
    }

    /// <summary>
    /// An alternative that carries no scopes still refuses one - the case that would have been open
    /// to everybody had an empty scope array been read as "requires nothing".
    /// </summary>
    /// <remarks>
    /// The document offers two ways in: an OAuth token carrying <c>pets:read</c>, or an API key.
    /// Neither is "nobody", so the OR must not be satisfied by an anonymous caller. Reading the
    /// unscoped alternative as the absence of a requirement would have made this route public while
    /// its description said otherwise.
    /// </remarks>
    [HardenedTest]
    public async Task AnUnscopedAlternativeStillRefusesAnAnonymousCaller(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/secured/either");

        Assert.Equal(401, response.StatusCode);
    }

    /// <summary>
    /// A route the description declares public is left alone, and answers.
    /// </summary>
    /// <remarks>
    /// <c>listStores</c> declares <c>security: []</c>. That derives no requirement rather than
    /// deriving <c>[AllowAnonymous]</c> - a described requirement is conjoined with what the handler
    /// declared, so it may narrow a route and must never open one - and with nothing else guarding
    /// this application the route answers.
    /// </remarks>
    [HardenedTest]
    public async Task ARouteDeclaredPublicIsNotGuarded(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/stores");

        response.Assert.Ok();
    }

    /// <summary>
    /// A route the description says nothing about is untouched.
    /// </summary>
    [HardenedTest]
    public async Task ARouteWithNoDeclaredSecurityIsNotGuarded(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();
    }
}
