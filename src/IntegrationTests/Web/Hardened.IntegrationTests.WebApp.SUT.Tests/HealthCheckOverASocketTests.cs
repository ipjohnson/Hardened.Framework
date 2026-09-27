using System.Text.Json;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Kestrel.Runtime;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The probes over a socket, where the response body is the server's own stream.
/// </summary>
/// <remarks>
/// Kestrel refuses a synchronous write to that stream, and the pipeline host writes to a memory
/// stream that accepts one, so a probe that answered on the pipeline answered an empty body here.
/// </remarks>
[KestrelRuntime]
public class HealthCheckOverASocketTests
{
    [ModuleTest]
    public Task BothProbesSendTheirBody(ITestWebApp testWebApp) => Probe(testWebApp);

    /// <summary>ASP.NET Core refuses a synchronous write the same way.</summary>
    [ModuleTest]
    [AspNetCoreRuntime]
    public Task BothProbesSendTheirBodyThroughAspNetCore(ITestWebApp testWebApp) =>
        Probe(testWebApp);

    private static async Task Probe(ITestWebApp testWebApp)
    {
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            var response = await testWebApp.Get(path);

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(
                "Healthy",
                JsonDocument
                    .Parse(await response.ReadTextAsync())
                    .RootElement.GetProperty("status")
                    .GetString()
            );
        }
    }
}
