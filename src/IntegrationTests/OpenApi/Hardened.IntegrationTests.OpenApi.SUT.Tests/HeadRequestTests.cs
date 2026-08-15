namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// HEAD through the specification-first routing table.
///
/// <para>
/// There are two routing table generators, and the one compiled from an OpenAPI document is a
/// near-copy of the attribute-routed one. Part 0 of this work fixed the token-bound defect in
/// both on the grounds that leaving them disagreeing is worse than both being wrong the same way;
/// the same holds here. An application generated from a document should answer <c>curl -I</c>
/// exactly as one written with attributes does.
/// </para>
/// </summary>
public class HeadRequestTests {

    [HardenedTest]
    public async Task Head_ReachesTheGetHandlerBehindAPathParameter(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, "/pets/42");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task Head_ReachesTheGetHandlerOnATokenlessRoute(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, "/pets");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task Head_WritesNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("HEAD", null, "/pets");

        Assert.Equal(0, response.Body.Length);
    }

    [HardenedTest]
    public async Task Head_ReportsTheLengthTheGetWouldHaveWritten(ITestWebApp testWebApp) {
        var get = await testWebApp.Get("/pets");
        var head = await testWebApp.Request("HEAD", null, "/pets");

        Assert.Equal(
            get.Body.Length.ToString(),
            head.Headers["Content-Length"].ToString());
    }
}
