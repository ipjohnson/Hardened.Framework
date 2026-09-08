using Azure.Messaging.ServiceBus;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.AzureEvents.Host.Tests;

/// <summary>
/// The events fixture served by the real Functions host, with the topic on the real Service Bus
/// emulator.
/// </summary>
/// <remarks>
/// One set of containers per class, for the reason the queue fixture's host tests give, and the
/// same trait: these need Docker and four pulled images, and fail rather than skip without them.
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class HostImageTests : IClassFixture<HostImageTests.FunctionApp> {
    private readonly FunctionApp _app;

    public HostImageTests(FunctionApp app) {
        _app = app;
    }

    /// <summary>
    /// Four triggers, four functions, all indexed from the generated provider - including the
    /// Event Grid function no emulator drives and the timer whose schedule is an app setting.
    /// </summary>
    [Fact]
    public async Task TheHostIndexesEveryFunctionTheProviderDeclares() {
        var log = await _app.Simulator.HostLogContaining(
            "Found the following functions:", cancellationToken: TestContext.Current.CancellationToken);

        const string prefix = "Host.Functions.";

        var indexed = log.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(prefix, StringComparison.Ordinal))
            .Select(line => line.Substring(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Trim())
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(["Event", "Queue_orders_new", "Timer_nightly_rollup", "Topic_order_events"], indexed);
    }

    /// <summary>
    /// The whole deployed path for a topic: a message published to it, delivered to the
    /// subscription the application named, received by the host's Service Bus extension, bound
    /// through the extension's converter, run through the generated executor and shim, forked by
    /// the batch filter, routed as a topic rather than a queue, and reported on the container's
    /// output.
    /// </summary>
    [Fact]
    public async Task AMessagePublishedToTheTopicReachesTheTopicHandler() {
        await _app.Publish(EventsHostSimulator.Topic, """{"id":"host-t-1","quantity":2}""");

        var observed = await _app.Simulator.Observed.WaitFor(
            one => one.Has("id", "host-t-1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("topic", observed.Get("kind"));

        await _app.Simulator.HostLogContaining(
            "Executed 'Functions.Topic_order_events' (Succeeded", cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The queue beside the topic, in the same function app, routing to its own handler.
    /// </summary>
    [Fact]
    public async Task AMessagePublishedToTheQueueReachesTheQueueHandler() {
        await _app.Publish(EventsHostSimulator.Queue, """{"id":"host-q-1","quantity":1}""");

        var observed = await _app.Simulator.Observed.WaitFor(
            one => one.Has("id", "host-q-1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("queue", observed.Get("kind"));

        await _app.Simulator.HostLogContaining(
            "Executed 'Functions.Queue_orders_new' (Succeeded", cancellationToken: TestContext.Current.CancellationToken);
    }

    public sealed class FunctionApp : IAsyncLifetime {
        public EventsHostSimulator Simulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.AzureEvents.SUT"));

        private ServiceBusClient? _client;

        public async ValueTask InitializeAsync() {
            await Simulator.StartAsync(TestContext.Current.CancellationToken);

            _client = new ServiceBusClient(Simulator.PublisherConnectionString);
        }

        /// <summary>Publishes one JSON message to a queue or a topic, as an application would.</summary>
        public async Task Publish(string entity, string body) {
            await using var sender = _client!.CreateSender(entity);

            await sender.SendMessageAsync(
                new ServiceBusMessage(body) { ContentType = "application/json" },
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            if (_client != null) {
                await _client.DisposeAsync();
            }

            await Simulator.DisposeAsync();
        }
    }
}
