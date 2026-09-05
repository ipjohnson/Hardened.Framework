using Hardened.Shared.Testing.Impl;
using Hardened.Web.Testing.Tests.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>
/// <see cref="TestWebApp"/> built by hand from a root provider, the way a test with no runner
/// attribute would build it, over the same substitute pipeline the handler tests use.
/// </summary>
public class TestWebAppTests {

    private static (SubstitutePipeline Host, TestWebApp App) Build(Func<Requests.Abstract.Execution.IExecutionContext, Task>? handler = null) {
        var host = new SubstitutePipeline(handler);

        return (host, new TestWebApp(new ServiceProviderApplicationRoot(host.Provider), NullLogger.Instance));
    }

    [Fact]
    public async Task PutSendsThePutMethod() {
        var (host, app) = Build();

        var response = await app.Put("body", "/things/1");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("PUT", Assert.Single(host.Contexts).Request.Method);
    }

    [Fact]
    public async Task ABodyGivenAsMemoryGoesOnTheWireAsItsBytes() {
        var (host, app) = Build();
        var bytes = "raw"u8.ToArray();

        await app.Post(new ReadOnlyMemory<byte>(bytes), "/things");

        var body = Assert.Single(host.Contexts).Request.Body;

        body.Position = 0;

        using var reader = new StreamReader(body);

        Assert.Equal("raw", await reader.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A harness built by hand knows no test assembly, so the factory is looked for in the assembly
    /// that is calling - this one, which declares <see cref="AdaptedClientFactory"/>.
    /// </summary>
    [Fact]
    public void AClientBuiltByHandFindsTheFactoryInTheCallingAssembly() {
        var (_, app) = Build();

        var client = app.CreateClient<AdaptedClient>(new TestCredential(new[] { "x" }));

        Assert.Equal("http://harness/", client.Http.BaseAddress!.ToString());
        Assert.Equal("x", client.Http.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
    }
}
