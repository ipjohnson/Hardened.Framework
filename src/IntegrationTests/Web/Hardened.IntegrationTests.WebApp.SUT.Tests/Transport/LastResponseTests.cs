using System.Net;
using System.Text;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The response the pipeline answered last in this test, whichever door it went out of.
/// </summary>
/// <remarks>
/// This class and <see cref="LastResponseIsolationTests"/> run in parallel and read their own
/// answers, which is the confirmation the design asked for: the DependencyModules runner leaves
/// xUnit's <c>TestContext.Current</c> in place around the harness's startup and the test body.
/// </remarks>
public class LastResponseTests {

    [HardenedTest]
    public async Task AfterAClientCallItReportsWhatThePipelineAnswered(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(401, LastResponse.Status);
        Assert.True(LastResponse.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Equal((int)response.StatusCode, LastResponse.Status);
    }

    [HardenedTest]
    [Grants("pets:read")]
    public async Task AfterAHarnessCallItReportsTheSame(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        Assert.Equal(200, LastResponse.Status);
        Assert.Equal(response.StatusCode, LastResponse.Status);
        Assert.StartsWith("application/json", LastResponse.ContentType);
        Assert.Contains("pets", Encoding.UTF8.GetString(LastResponse.Body));
    }

    [HardenedTest]
    public async Task ACreatedStatusTheClientSwallowsIsStillReported(ITestWebApp app) {
        using var client = app.CreateHttpClient();
        using var response = await client.PostAsync("/verbs/created", new StringContent(""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(201, LastResponse.Status);
    }

    [HardenedTest]
    public async Task ItIsTheLastResponseNotTheFirst(ITestWebApp app) {
        await app.Get("/authorization/pets");
        await app.Get("/authorization/open");

        Assert.Equal(200, LastResponse.Status);
    }

    [HardenedTest]
    public void ReadingItBeforeAnyRequestFailsNamingTheTest() {
        var failure = Assert.Throws<InvalidOperationException>(() => LastResponse.Status);

        Assert.Contains(nameof(ReadingItBeforeAnyRequestFailsNamingTheTest), failure.Message);
        Assert.False(LastResponse.IsAvailable);
    }
}

/// <summary>Reads its own answer while <see cref="LastResponseTests"/> reads its.</summary>
public class LastResponseIsolationTests {

    [HardenedTest]
    public async Task AParallelTestSeesOnlyItsOwnResponse(ITestWebApp app) {
        for (var round = 0; round < 20; round++) {
            var response = await app.Get("/authorization/open");

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(200, LastResponse.Status);
            Assert.False(LastResponse.Headers.ContainsKey("WWW-Authenticate"));
        }
    }
}
