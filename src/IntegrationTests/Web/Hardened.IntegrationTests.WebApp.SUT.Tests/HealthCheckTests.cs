using System.Text.Json;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The health endpoints, end to end through a real application.
/// </summary>
/// <remarks>
/// The provider is registered by <c>HardenedWebModule</c> rather than by anything this application
/// wrote, so what is under test is partly the registration: an application that imports the web
/// module gets these paths without asking, and that only holds if the provider really is in the
/// chain the host builds.
/// </remarks>
public class HealthCheckTests {

    [HardenedTest]
    public async Task LivenessAnswers200(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/health/live");

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>
    /// This application registers no checks, which is ready rather than unhealthy - it has said it
    /// has nothing to verify, not that something is wrong.
    /// </summary>
    [HardenedTest]
    public async Task ReadinessAnswers200WithNoChecksRegistered(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/health/ready");

        Assert.Equal(200, response.StatusCode);
    }

    [HardenedTest]
    public async Task TheBodyIsJsonNamingTheStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/health/ready");

        var status = JsonDocument.Parse(response.Body!).RootElement.GetProperty("status").GetString();

        Assert.Equal("Healthy", status);
    }

    /// <summary>
    /// A cached readiness answer reports the instance's state at some earlier moment to a load
    /// balancer deciding now, which is worse than no answer.
    /// </summary>
    [HardenedTest]
    public async Task TheResponseIsNotCacheable(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/health/ready");

        Assert.Equal("no-store", response.Headers["Cache-Control"].ToString());
    }

    /// <summary>
    /// Health is not a write endpoint, and declining rather than answering leaves the path free.
    /// </summary>
    [HardenedTest]
    public async Task AWriteVerbIsNotAHealthProbe(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("POST", null, "/health/live");

        Assert.NotEqual(200, response.StatusCode);
    }
}
