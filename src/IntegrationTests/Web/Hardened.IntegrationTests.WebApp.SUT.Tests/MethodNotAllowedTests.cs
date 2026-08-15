namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A request to a real resource with the wrong verb, end to end.
/// </summary>
/// <remarks>
/// It came back 404, indistinguishable from a request to a URL nobody declared - even though the
/// routing table knew, having matched the path and then fallen through the verb switch. Every peer
/// except Express returns 405, it is in RFC 9110, API Gateway and CloudFront cache the two
/// differently, and generated clients expect it.
/// </remarks>
public class MethodNotAllowedTests {

    [HardenedTest]
    public async Task AVerbWithNoRouteOnAKnownPathIs405(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/binding/path/42");

        Assert.Equal(405, response.StatusCode);
    }

    /// <summary>
    /// With <c>Allow</c>. RFC 9110 requires it, and it is the only thing that makes the response
    /// actionable rather than merely correct.
    /// </summary>
    [HardenedTest]
    public async Task The405CarriesTheVerbsThePathDoesAnswer(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/verbs/item/abc123");

        var allow = response.Headers["Allow"].ToString();

        Assert.Contains("GET", allow);
        Assert.Contains("DELETE", allow);
        Assert.Contains("PATCH", allow);
        Assert.DoesNotContain("PUT", allow);
    }

    /// <summary>
    /// HEAD is in it, because the fall-through means a client may call it.
    /// </summary>
    [HardenedTest]
    public async Task TheAllowHeaderIncludesHead(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/binding/path/42");

        Assert.Contains("HEAD", response.Headers["Allow"].ToString());
    }

    /// <summary>
    /// A path nobody declared is still 404. The distinction between "no such URL" and "not with
    /// that verb" is the whole point of the change.
    /// </summary>
    [HardenedTest]
    public async Task AnUndeclaredPathIsStill404(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/nothing/here/at/all");

        response.Assert.NotFound();
    }

    /// <summary>
    /// And a verb that does have a route is unaffected, which is the regression worth guarding:
    /// the leaf switch answers both cases and the 405 arm runs after every other verb has been
    /// tested.
    /// </summary>
    [HardenedTest]
    public async Task AVerbThatDoesHaveARouteStillReachesIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/verbs/item/abc123");

        response.Assert.Ok();
        Assert.Equal("deleted:abc123", response.Deserialize<string>());
    }

    /// <summary>
    /// Nothing is written. A 405 is a status and a header; serializing a null response value would
    /// answer with whatever the client's Accept happened to match.
    /// </summary>
    [HardenedTest]
    public async Task The405WritesNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/binding/path/42");

        Assert.Equal(0, response.Body.Length);
    }
}
