using System.Net;
using Hardened.Gcp.CloudRun.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunEvent.Simulator.Tests;

/// <summary>
/// The event fixture in the image Cloud Run runs, reached by the CloudEvent Eventarc would post.
/// </summary>
/// <remarks>
/// Eventarc has no emulator, so the event is hand-built in binary mode - the matrix's honest △ -
/// and what the tier adds over the envelope tests is the real image, the real socket and the real
/// process.
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class CloudEventRequestTests : IClassFixture<CloudEventRequestTests.Service> {
    private readonly Service _service;

    public CloudEventRequestTests(Service service) {
        _service = service;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABinaryCloudEventReachesTheHandlerInTheContainer() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/", """{"id":"c-1","quantity":5}""", "application/json", Token,
            ("ce-specversion", "1.0"),
            ("ce-id", "1234-1234-1234"),
            ("ce-source", "com.acme.orders"),
            ("ce-type", "OrderPlaced"),
            ("ce-time", "2026-09-07T10:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var observed = await _service.Container.Observed.WaitFor(one => one.Has("entry", "event:c-1"), cancellationToken: Token);

        Assert.Equal("event", observed.Get("kind"));
    }

    /// <summary>An event no handler declared is answered as a failure Eventarc will retry and report, not swallowed.</summary>
    [Fact]
    public async Task AnEventNoHandlerDeclaredIsAFailure() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/", """{"id":"c-2"}""", "application/json", Token,
            ("ce-specversion", "1.0"),
            ("ce-id", "5678"),
            ("ce-source", "com.acme.orders"),
            ("ce-type", "OrderCancelled"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public sealed class Service : IAsyncLifetime {
        public CloudRunService Container { get; } = CloudRunService.For("Hardened.IntegrationTests.CloudRunEvent.SUT");

        public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Container.DisposeAsync();
    }
}
