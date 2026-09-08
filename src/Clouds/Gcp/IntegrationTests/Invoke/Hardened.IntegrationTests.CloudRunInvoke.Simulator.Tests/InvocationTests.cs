using System.Net;
using System.Text.Json;
using Hardened.Gcp.CloudRun.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunInvoke.Simulator.Tests;

/// <summary>
/// The invoke fixture in the image Cloud Run runs, invoked over real HTTP the way a caller would.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class InvocationTests : IClassFixture<InvocationTests.Service> {
    private readonly Service _service;

    public InvocationTests(Service service) {
        _service = service;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The caller's payload reaches the handler and the handler's return value is the body the caller reads.</summary>
    [Fact]
    public async Task AnInvocationOverHttpAnswersWithTheHandlersReturnValue() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/_triggers/invoke/Handle", """{"id":"c-1","quantity":2}""", "application/json", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal("c-1", receipt.RootElement.GetProperty("id").GetString());
        Assert.Equal("placed", receipt.RootElement.GetProperty("status").GetString());

        var observed = await _service.Container.Observed.WaitFor(one => one.Has("id", "c-1"), cancellationToken: Token);

        Assert.Equal("invoke", observed.Get("kind"));
    }

    /// <summary>A path outside the invoke route is a web 404, not an invocation of anything.</summary>
    [Fact]
    public async Task APostOutsideTheInvokeRouteIsNotAnInvocation() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/Handle", """{"id":"c-2"}""", "application/json", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public sealed class Service : IAsyncLifetime {
        public CloudRunService Container { get; } = CloudRunService.For("Hardened.IntegrationTests.CloudRunInvoke.SUT");

        public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Container.DisposeAsync();
    }
}
