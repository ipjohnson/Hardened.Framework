using Azure.Messaging.ServiceBus;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.Host.Tests;

/// <summary>
/// The queue fixture served by the real Functions host, from the real Service Bus emulator.
/// </summary>
/// <remarks>
/// <para>
/// One set of containers per class rather than per test: the host takes a minute to start under
/// emulation, and the tests here read their own observations by order id.
/// </para>
/// <para>
/// <c>Simulator</c> is the trait the CI split reads: these need Docker and four pulled images, and
/// they fail rather than skip without either, as every Docker test in this repository does.
/// </para>
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class HostImageTests : IClassFixture<HostImageTests.Function> {
    private readonly Function _function;

    public HostImageTests(Function function) {
        _function = function;
    }

    /// <summary>
    /// The host indexes exactly the functions the generated provider declares. With worker
    /// indexing on, the host asks the worker, and what the worker answers is what the provider
    /// returned - so the list the host logs is the provider's, and it has to hold the one queue
    /// function and nothing else.
    /// </summary>
    [Fact]
    public async Task TheHostIndexesExactlyTheFunctionsTheProviderDeclares() {
        var log = await _function.Simulator.HostLogContaining(
            "Found the following functions:", cancellationToken: TestContext.Current.CancellationToken);

        // The host lists each function on its own line as Host.Functions.<name> under that
        // heading, and nowhere else. Found by substring rather than at the start of the line,
        // because Docker prefixes every log line with its timestamp.
        const string prefix = "Host.Functions.";

        var indexed = log.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(prefix, StringComparison.Ordinal))
            .Select(line => line.Substring(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Trim())
            .Distinct()
            .ToArray();

        Assert.Equal(["Queue_orders"], indexed);
    }

    /// <summary>
    /// The whole deployed path: a message published to the emulator, received by the host's
    /// Service Bus extension, sent to the worker, bound through the extension's converter, run
    /// through Hardened's generated executor and shim, forked by the batch filter, bound by the
    /// handler's binder, and reported on the container's output. Nothing here is in-process.
    /// </summary>
    [Fact]
    public async Task AMessagePublishedToTheEmulatorReachesTheHandler() {
        await _function.Publish("""{"id":"host-1","quantity":4}""");

        var observed = await _function.Simulator.Observed.WaitFor(
            one => one.Has("id", "host-1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("queue", observed.Get("kind"));
        Assert.Equal(4, observed.Fields.GetProperty("quantity").GetInt32());

        // Succeeded, as the host reports it: the worker answered the invocation rather than only
        // printing on its way to failing it.
        await _function.Simulator.HostLogContaining(
            "Executed 'Functions.Queue_orders' (Succeeded", cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A handler that throws fails the invocation, which is what makes the extension abandon the
    /// batch, and the queue delivers the message again. The second observation of the same order
    /// is the evidence; the emulator is configured to deliver three times before dead-lettering.
    /// </summary>
    [Fact]
    public async Task AHandlerThatThrowsAbandonsTheBatchAndTheMessageIsRedelivered() {
        await _function.Publish("""{"id":"host-refused","quantity":-1}""");

        var observations = await Redelivered("host-refused", 2, TestContext.Current.CancellationToken);

        Assert.True(observations.Count >= 2, "the refused order was handled once and never delivered again");

        await _function.Simulator.HostLogContaining(
            "Executed 'Functions.Queue_orders' (Failed", cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every observation of <paramref name="id"/> once at least <paramref name="count"/> exist.
    /// </summary>
    private async Task<IReadOnlyList<Observation>> Redelivered(string id, int count, CancellationToken cancellationToken) {
        var deadline = DateTime.UtcNow + ObservedInvocations.DefaultTimeout;

        while (true) {
            var matching = (await _function.Simulator.Observed.Current(cancellationToken))
                .Where(one => one.Has("id", id))
                .ToList();

            if (matching.Count >= count) {
                return matching;
            }

            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException(
                    $"Waited for {count} deliveries of '{id}' and saw {matching.Count}. The host printed:\n" +
                    await _function.Simulator.HostLog(cancellationToken));
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    public sealed class Function : IAsyncLifetime {
        public FunctionsHostSimulator Simulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.AzureQueue.SUT"));

        private ServiceBusClient? _client;

        public async ValueTask InitializeAsync() {
            await Simulator.StartAsync(TestContext.Current.CancellationToken);

            _client = new ServiceBusClient(Simulator.PublisherConnectionString);
        }

        /// <summary>Publishes one JSON message to the queue, as an application would.</summary>
        public async Task Publish(string body) {
            await using var sender = _client!.CreateSender(FunctionsHostSimulator.Queue);

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
