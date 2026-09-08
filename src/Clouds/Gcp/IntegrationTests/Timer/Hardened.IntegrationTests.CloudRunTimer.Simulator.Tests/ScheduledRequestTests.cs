using System.Net;
using Hardened.Gcp.CloudRun.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunTimer.Simulator.Tests;

/// <summary>
/// The scheduled fixture in the image Cloud Run runs, reached by the request Cloud Scheduler sends.
/// </summary>
/// <remarks>
/// Scheduler has no emulator, so the request is hand-built - the matrix's honest △ - and what the
/// tier adds over the envelope tests is the real image, the real socket and the real process.
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class ScheduledRequestTests : IClassFixture<ScheduledRequestTests.Service> {
    private readonly Service _service;

    public ScheduledRequestTests(Service service) {
        _service = service;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASchedulerRequestReachesTheTimerHandlerInTheContainer() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/_triggers/timer/nightly-rollup", null, "application/json", Token,
            ("X-CloudScheduler", "true"),
            ("X-CloudScheduler-JobName", "nightly-rollup"),
            ("X-CloudScheduler-ScheduleTime", "2026-09-07T02:00:00Z"),
            ("User-Agent", "Google-Cloud-Scheduler"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var observed = await _service.Container.Observed.WaitFor(one => one.Has("entry", "timer:nightly-rollup"), cancellationToken: Token);

        Assert.Equal("timer", observed.Get("kind"));
    }

    /// <summary>A job wired to another timer's URL is answered as a failure Scheduler will report, not run under the wrong name.</summary>
    [Fact]
    public async Task AJobPostingToAnotherTimersUrlIsRefused() {
        using var response = await _service.Container.SendAsync(
            HttpMethod.Post, "/_triggers/timer/nightly-rollup", null, "application/json", Token,
            ("X-CloudScheduler-JobName", "hourly-sweep"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public sealed class Service : IAsyncLifetime {
        public CloudRunService Container { get; } = CloudRunService.For("Hardened.IntegrationTests.CloudRunTimer.SUT");

        public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Container.DisposeAsync();
    }
}
