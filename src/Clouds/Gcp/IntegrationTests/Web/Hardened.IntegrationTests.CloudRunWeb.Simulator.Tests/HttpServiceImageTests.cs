using System.Net;
using System.Text.Json;
using Hardened.Gcp.CloudRun.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunWeb.Simulator.Tests;

/// <summary>
/// The web fixture in the image Cloud Run runs, reached over real HTTP: the matrix's ✓ for HTTP.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class HttpServiceImageTests : IClassFixture<HttpServiceImageTests.Service> {
    private readonly Service _service;

    public HttpServiceImageTests(Service service) {
        _service = service;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGetReachesItsHandlerInTheContainer() {
        using var response = await _service.Container.SendAsync(HttpMethod.Get, "/orders/o-1", null, "application/json", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var order = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal("o-1", order.RootElement.GetProperty("id").GetString());
        Assert.Equal(7, order.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task APostBindsItsBodyInTheContainer() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/orders", """{"id":"o-2","quantity":3}""", "application/json", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var order = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal(3, order.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AnUnmatchedPathIsA404InTheContainer() {
        using var response = await _service.Container.SendAsync(HttpMethod.Get, "/nothing-here", null, "application/json", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public sealed class Service : IAsyncLifetime {
        public CloudRunService Container { get; } = CloudRunService.For("Hardened.IntegrationTests.CloudRunWeb.SUT");

        public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Container.DisposeAsync();
    }
}
