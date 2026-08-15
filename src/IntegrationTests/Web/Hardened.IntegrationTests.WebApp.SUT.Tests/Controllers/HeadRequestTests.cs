namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// HEAD against a GET route, end to end.
///
/// Before the routing table carried a fall-through case, a HEAD matched nothing and every
/// endpoint in every Hardened application answered <c>curl -I</c> with a 404 - including the ones
/// health checkers, link validators and CDNs probe that way.
///
/// The tests that compare against a GET are the ones that matter: RFC 9110 requires the HEAD
/// response to carry the header fields the GET would have carried, which is only true because the
/// handler runs in full and the body is discarded on the way out.
/// </summary>
public class HeadRequestTests {
    private const string Path = "/binding/path/42";

    [HardenedTest]
    public async Task Head_ReachesTheGetHandler(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, Path);

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task Head_WritesNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, Path);

        Assert.Equal(0, response.Body.Length);
    }

    [HardenedTest]
    public async Task Head_ReportsTheLengthTheGetWouldHaveWritten(ITestWebApp testWebApp) {
        var get = await testWebApp.Get(Path);
        var head = await testWebApp.Request("HEAD", null, Path);

        Assert.Equal(
            get.Body.Length.ToString(),
            head.Headers["Content-Length"].ToString());
    }

    [HardenedTest]
    public async Task Head_CarriesTheContentTypeOfTheGet(ITestWebApp testWebApp) {
        var get = await testWebApp.Get(Path);
        var head = await testWebApp.Request("HEAD", null, Path);

        Assert.Equal(
            get.Headers["Content-Type"].ToString(),
            head.Headers["Content-Type"].ToString());
    }

    /// <summary>
    /// A route reached through a wildcard node takes a different path through the generated
    /// table - a switch inside the wildcard match method rather than the leaf switch - so the
    /// fall-through has to be emitted in both.
    /// </summary>
    [HardenedTest]
    public async Task Head_ReachesAHandlerBehindAWildcardNode(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, "/verbs/item/abc123");

        response.Assert.Ok();
    }
}
