using System.Net;
using System.Text;
using DotNet.Testcontainers.Builders;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunQueue.Simulator.Tests;

/// <summary>
/// What happens to a request in flight when Cloud Run stops the instance.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run sends <c>SIGTERM</c> and, ten seconds later, <c>SIGKILL</c>. A push the service was
/// still handling when the signal arrived has those ten seconds to be answered; a service that
/// exits on the signal drops it, and Pub/Sub redelivers a message the handler may already have
/// half applied. Docker's <c>stop</c> is the same pair with the same ten seconds, so stopping the
/// container is the contract exactly.
/// </para>
/// <para>
/// Its own container rather than the class fixture the push tests share, because it stops it.
/// </para>
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class ShutdownTests {
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A push whose handler takes three seconds, a stop half a second in, and then: the response
    /// is a 200, the handler's line was printed, and the process exited on its own with 0 rather
    /// than being killed.
    /// </summary>
    [Fact]
    public async Task SigtermLetsAnInFlightRequestFinish() {
        await using var network = new NetworkBuilder().Build();

        await network.CreateAsync(Token);

        await using var service = new CloudRunService(
            network,
            ApplicationOutput.Of("Hardened.IntegrationTests.CloudRunQueue.SUT"),
            "Hardened.IntegrationTests.CloudRunQueue.SUT");

        await service.StartAsync(Token);

        using var client = new HttpClient { BaseAddress = service.HostAddress, Timeout = TimeSpan.FromSeconds(60) };

        var inFlight = client.PostAsync(
            "/", new StringContent(Push("slow-1"), Encoding.UTF8, "application/json"), Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);

        var stopping = service.StopAsync(Token);

        using var response = await inFlight;

        await stopping;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var observed = await service.Observed.Current(Token);

        Assert.Contains(observed, one => one.Has("id", "slow-1"));
        Assert.Equal(0, await service.ExitCodeAsync(Token));
    }

    /// <summary>The push Pub/Sub would send for an order with <paramref name="id"/>.</summary>
    private static string Push(string id) {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes($$"""{"id":"{{id}}","quantity":1}"""));

        return $$"""
            {"message":{"data":"{{data}}","messageId":"{{id}}","message_id":"{{id}}",
             "publishTime":"2026-01-01T00:00:00.000Z","publish_time":"2026-01-01T00:00:00.000Z"},
             "subscription":"projects/hardened-test/subscriptions/orders"}
            """;
    }
}
