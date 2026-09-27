using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.WebApp.SUT.Client;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// What a request keeps from the one before it, on a host that is not promised between them.
/// </summary>
/// <remarks>
/// <para>
/// The pair below is the whole boundary, asserted against the same handler. <c>/response-cache/uncached</c>
/// answers a counter held in a singleton and declares no caching, so it counts invocations against
/// one container and nothing else.
/// </para>
/// <para>
/// It matters because the arrangement it stands for is not exotic. Two requests to a deployed
/// function may be served by two execution environments or by one, and nothing says which, so a
/// handler leaning on what the previous request left in a singleton fails intermittently in
/// production and never here. Under the pipeline host it now fails here first.
/// </para>
/// </remarks>
public class ContainerIsolationTests
{
    /// <summary>
    /// Two requests, two containers, so the singleton behind the second has never been used.
    /// </summary>
    [ModuleTest]
    public async Task ARequestKeepsNothingFromTheOneBefore(ITestWebApp testWebApp)
    {
        var first = await testWebApp.Get("/response-cache/uncached");
        var second = await testWebApp.Get("/response-cache/uncached");

        Assert.Equal("1", first.Deserialize<string>());
        Assert.Equal("1", second.Deserialize<string>());
    }

    /// <summary>
    /// The escape hatch, for a test whose subject is the reuse itself.
    /// </summary>
    /// <remarks>
    /// The same handler and the same two requests, and the only difference is the attribute. A
    /// response cache serving a second read, a rate limiter tripping on the eleventh call and a
    /// connection held open are all this shape, and all of them are asking to model one warm
    /// environment rather than two cold ones.
    /// </remarks>
    [ModuleTest]
    public async Task SharedPutsEveryRequestOnOneContainer([Shared] ITestWebApp testWebApp)
    {
        var first = await testWebApp.Get("/response-cache/uncached");
        var second = await testWebApp.Get("/response-cache/uncached");

        Assert.Equal("1", first.Deserialize<string>());
        Assert.Equal("2", second.Deserialize<string>());
    }

    /// <summary>
    /// A client is a caller, so two of them marked shared are two callers on one environment rather
    /// than two environments.
    /// </summary>
    [ModuleTest]
    public async Task TwoSharedClientsReachTheSameContainer(
        [Shared] ITestWebApp first,
        [Shared] ITestWebApp second
    )
    {
        await first.Get("/response-cache/uncached");

        var seen = await second.Get("/response-cache/uncached");

        Assert.Equal("2", seen.Deserialize<string>());
    }

    /// <summary>
    /// A generated client is a caller like the harness, and isolated the same way.
    /// </summary>
    [ModuleTest]
    public async Task AGeneratedClientKeepsNothingFromTheRequestBefore(WebAppClient client)
    {
        Assert.Equal("1", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
        Assert.Equal("1", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
    }

    /// <summary>
    /// <c>[Shared]</c> on a Kiota client. The Kiota route builds its client over handlers of its
    /// own, and that chain used to be composed without the mark.
    /// </summary>
    [ModuleTest]
    public async Task ASharedGeneratedClientPutsEveryRequestOnOneContainer(
        [Shared] WebAppClient client
    )
    {
        Assert.Equal("1", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
        Assert.Equal("2", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
    }

    /// <summary>
    /// A credential attribute on the parameter is what builds its value, so it reads
    /// <c>[Shared]</c> beside it rather than leaving it to the harness.
    /// </summary>
    [ModuleTest]
    public async Task SharedBesideTheParametersOwnCredentialStillShares(
        [Shared, Grants("pets:read")] WebAppClient client,
        [Shared, Subject("pia")] ITestWebApp app,
        [Shared, Anonymous] HttpClient http
    )
    {
        Assert.Equal("1", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));
        Assert.Equal("2", (await app.Get("/response-cache/uncached")).Deserialize<string>());
        Assert.Equal("\"3\"", await http.GetStringAsync("/response-cache/uncached", Token));
    }

    /// <summary>
    /// A client a shared harness builds sends to the harness's container, whether the test built
    /// it by type or took the raw <see cref="HttpClient"/>.
    /// </summary>
    [ModuleTest]
    public async Task TheClientsASharedHarnessBuildsReachItsContainer([Shared] ITestWebApp app)
    {
        await app.Get("/response-cache/uncached");

        var client = app.CreateClient<WebAppClient>();

        Assert.Equal("2", await client.ResponseCache.Uncached.GetAsync(cancellationToken: Token));

        using var http = app.CreateHttpClient();

        Assert.Equal("\"3\"", await http.GetStringAsync("/response-cache/uncached", Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
