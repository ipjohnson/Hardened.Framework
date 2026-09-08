using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.AzureStream.Host.Tests;

/// <summary>
/// The stream fixture served by the real Functions host, from the real Event Hubs emulator.
/// </summary>
/// <remarks>
/// One set of containers per class, for the reason the queue fixture's host tests give, and the
/// same trait: these need Docker and three pulled images, and fail rather than skip without them.
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class HostImageTests : IClassFixture<HostImageTests.FunctionApp> {
    private readonly FunctionApp _app;

    public HostImageTests(FunctionApp app) {
        _app = app;
    }

    [Fact]
    public async Task TheHostIndexesExactlyTheFunctionsTheProviderDeclares() {
        var log = await _app.Simulator.HostLogContaining(
            "Found the following functions:", cancellationToken: TestContext.Current.CancellationToken);

        const string prefix = "Host.Functions.";

        var indexed = log.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(prefix, StringComparison.Ordinal))
            .Select(line => line.Substring(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Trim())
            .Where(name => name.Length > 0 && name.All(character => char.IsLetterOrDigit(character) || character == '_'))
            .Distinct()
            .ToArray();

        Assert.Equal(["Stream_clickstream"], indexed);
    }

    /// <summary>
    /// The whole deployed path: events published to the emulator, received by the host's Event
    /// Hubs extension off the partition, sent to the worker as a batch, bound through the
    /// extension's converter into the SDK's events, run through the generated executor and shim,
    /// forked by the batch filter in partition order, bound by the handler's binder from each
    /// event's own bytes, and reported on the container's output. Nothing here is in-process.
    /// </summary>
    [Fact]
    public async Task EventsPublishedToTheEmulatorReachTheHandlerInOrder() {
        await _app.Publish("""{"id":"host-1","count":1}""", """{"id":"host-2","count":2}""");

        await _app.Simulator.Observed.WaitFor(
            one => one.Has("id", "host-2"), cancellationToken: TestContext.Current.CancellationToken);

        var observed = (await _app.Simulator.Observed.Current(TestContext.Current.CancellationToken))
            .Where(one => one.Get("kind") == "stream")
            .Select(one => one.Get("id") ?? "")
            .ToArray();

        Assert.Equal(["host-1", "host-2"], observed);

        await _app.Simulator.HostLogContaining(
            "Executed 'Functions.Stream_clickstream' (Succeeded", cancellationToken: TestContext.Current.CancellationToken);
    }

    public sealed class FunctionApp : IAsyncLifetime {
        public StreamHostSimulator Simulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.AzureStream.SUT"));

        private EventHubProducerClient? _producer;

        public async ValueTask InitializeAsync() {
            await Simulator.StartAsync(TestContext.Current.CancellationToken);

            _producer = new EventHubProducerClient(Simulator.PublisherConnectionString, StreamHostSimulator.Hub);
        }

        /// <summary>Publishes JSON events to the hub in one batch, as an application would.</summary>
        public async Task Publish(params string[] bodies) {
            using var batch = await _producer!.CreateBatchAsync(TestContext.Current.CancellationToken);

            foreach (var body in bodies) {
                var eventData = new EventData(body) { ContentType = "application/json" };

                Assert.True(batch.TryAdd(eventData), "the event did not fit the batch");
            }

            await _producer.SendAsync(batch, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            if (_producer != null) {
                await _producer.DisposeAsync();
            }

            await Simulator.DisposeAsync();
        }
    }
}
