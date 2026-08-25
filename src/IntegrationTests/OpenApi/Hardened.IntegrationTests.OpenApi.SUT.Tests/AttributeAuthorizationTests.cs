namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// An <c>[AuthorizeGrants]</c> written on a contract-first handler, over a real request.
/// </summary>
/// <remarks>
/// <para>
/// <c>described-authorization.md</c> promises this: a described requirement arrives as one more
/// entry in the handler's metadata <em>alongside anything the implementation declared</em>, so a
/// contract may narrow a route and can never widen one. <c>security: []</c> does not strip an
/// attribute somebody wrote on the handler.
/// </para>
/// <para>
/// The guarantee was reported as broken in contract-first mode - the claim being that no path
/// existed from a C# attribute into the generated <c>_metadata</c>, so the attribute compiled, read
/// as protective in review, and enforced nothing. The path does exist. What did not exist was a
/// test: every existing case guarded a route <em>from</em> the description, and
/// <c>SecuredServiceImpl</c> says in its own remarks that neither of its handlers carries an
/// authorization attribute. Nothing in the suite would have contradicted the claim.
/// </para>
/// <para>
/// Driven over HTTP rather than asserted against the generated source, because the generated source
/// having the right characters in it is not the thing promised. What is promised is that the
/// request is refused.
/// </para>
/// </remarks>
public class AttributeAuthorizationTests {

    /// <summary>
    /// The attribute guards the route, even though the description declares it public.
    /// </summary>
    [HardenedTest]
    public async Task AnAttributeOnTheHandlerGuardsADescribedPublicRoute(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/guarded/by-attribute");

        Assert.Equal(401, response.StatusCode);
    }

    /// <summary>
    /// And the neighbouring route, which the description also declares public and which carries no
    /// attribute, still answers.
    /// </summary>
    /// <remarks>
    /// The control. Without it a test above would pass just as well if this application had turned
    /// default-deny on, which would make it a test of the posture rather than of the attribute.
    /// </remarks>
    [HardenedTest]
    public async Task ADescribedPublicRouteWithNoAttributeStillAnswers(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/stores");

        response.Assert.Ok();
    }
}
