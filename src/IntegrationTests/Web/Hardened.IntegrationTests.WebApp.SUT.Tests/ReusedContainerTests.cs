using Hardened.IntegrationTests.WebApp.SUT.Client;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A pipeline declared <see cref="TestContainerPolicy.Reused"/>: every request of a test reaches the
/// test's own container, with no socket and no <c>[Shared]</c> on the parameter.
/// </summary>
/// <remarks>
/// The same handler <see cref="ContainerIsolationTests"/> reads. <c>/response-cache/uncached</c>
/// answers a counter held in a singleton, so it counts the requests one container served. Declared
/// on the class here so the rest of the assembly keeps the default; an application that runs as one
/// long-lived process declares it once on the assembly.
/// </remarks>
[PipelineHost(ContainerPolicy = TestContainerPolicy.Reused)]
public class ReusedContainerTests
{
    [ModuleTest]
    public async Task EveryRequestReachesTheTestsOwnContainer(ITestWebApp testWebApp)
    {
        var first = await testWebApp.Get("/response-cache/uncached");
        var second = await testWebApp.Get("/response-cache/uncached");

        Assert.Equal("1", first.Deserialize<string>());
        Assert.Equal("2", second.Deserialize<string>());
    }

    [ModuleTest]
    public async Task AGeneratedClientAndAnHttpClientReachItToo(
        WebAppClient client,
        HttpClient http
    )
    {
        Assert.Equal("1", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
        Assert.Equal("\"2\"", await http.GetStringAsync("/response-cache/uncached", Token));
    }

    [ModuleTest]
    public async Task TheHostSaysItReuses(ITestHost host)
    {
        await Task.CompletedTask;

        Assert.Equal(TestContainerPolicy.Reused, host.ContainerPolicy);
    }

    /// <summary>
    /// A plain <c>[PipelineHost]</c> on the method is narrower than the class, so this test is back
    /// on a container per request.
    /// </summary>
    [ModuleTest]
    [PipelineHost]
    public async Task APlainPipelineHostOnTheMethodRebuildsAgain(ITestWebApp testWebApp)
    {
        var first = await testWebApp.Get("/response-cache/uncached");
        var second = await testWebApp.Get("/response-cache/uncached");

        Assert.Equal("1", first.Deserialize<string>());
        Assert.Equal("1", second.Deserialize<string>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
