using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Google.Cloud.PubSub.V1;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunQueue.Simulator.Tests;

/// <summary>
/// The queue fixture served by the image Cloud Run runs, driven by the Pub/Sub emulator.
/// </summary>
/// <remarks>
/// <para>
/// One network, one service container and one emulator per class: the emulator's push
/// subscription points at the service by alias, a test publishes through Google's client and
/// reads what the handler printed out of the container's logs. Nothing here is in-process, and
/// nothing in the service knows it is not on Cloud Run.
/// </para>
/// <para>
/// <c>Simulator</c> is the trait the CI split reads: these need Docker and two images, and they
/// fail rather than skip without either, as every Docker test in this repository does.
/// </para>
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class PubSubPushTests : IClassFixture<PubSubPushTests.Stack> {
    private readonly Stack _stack;

    public PubSubPushTests(Stack stack) {
        _stack = stack;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole deployed path: the client publishes, the emulator pushes to the service, Kestrel
    /// builds the request, the front door unwraps it, the composed dispatch routes it to the
    /// function table, the binder binds the order and the handler runs. The line it prints is
    /// the evidence.
    /// </summary>
    [Fact]
    public async Task AMessagePublishedToTheEmulatorReachesTheHandlerInTheContainer() {
        await _stack.Emulator.PublishAsync(_stack.Topic, """{"id":"e-1","quantity":4}""", Token);

        var observed = await _stack.Service.Observed.WaitFor(one => one.Has("id", "e-1"), cancellationToken: Token);

        Assert.Equal("queue", observed.Get("kind"));
        Assert.Equal(4, observed.Fields.GetProperty("quantity").GetInt32());
    }

    /// <summary>
    /// A handler that throws is answered 500, which Pub/Sub reads as a negative acknowledgement,
    /// so the emulator pushes the same message again. The store prints its refusal before it
    /// throws, which is what makes a second attempt visible from outside.
    /// </summary>
    [Fact]
    public async Task AFailedHandlerIsRedelivered() {
        await _stack.Emulator.PublishAsync(_stack.Topic, """{"id":"e-refused","quantity":-1}""", Token);

        var attempts = await _stack.Service.Observed.WaitForMatching(
            one => one.Has("id", "e-refused"), count: 2, cancellationToken: Token);

        Assert.All(attempts, one => Assert.Equal("true", one.Get("refused")));
    }

    public sealed class Stack : IAsyncLifetime {
        private readonly INetwork _network = new NetworkBuilder().Build();

        public Stack() {
            Service = new CloudRunService(
                _network,
                ApplicationOutput.Of("Hardened.IntegrationTests.CloudRunQueue.SUT"),
                "Hardened.IntegrationTests.CloudRunQueue.SUT");
            Emulator = new PubSubEmulator(_network);
        }

        public CloudRunService Service { get; }

        public PubSubEmulator Emulator { get; }

        public TopicName Topic { get; private set; } = null!;

        public async ValueTask InitializeAsync() {
            await _network.CreateAsync(Token);

            await Task.WhenAll(Service.StartAsync(Token), Emulator.StartAsync(Token));

            // The subscription is named orders, which is what the handler declared; the topic's
            // name is the deployment's business and the handler never sees it.
            Topic = await Emulator.CreateTopicWithPushSubscription(
                "order-events", "orders", Service.PushEndpoint, Token);
        }

        public async ValueTask DisposeAsync() {
            await Emulator.DisposeAsync();
            await Service.DisposeAsync();
            await _network.DisposeAsync();
        }
    }
}

/// <summary>
/// Waiting for several observations that match, which the harness's <c>WaitFor</c> and
/// <c>WaitForCount</c> do not cover between them.
/// </summary>
internal static class ObservedInvocationsExtensions {
    public static async Task<IReadOnlyList<Observation>> WaitForMatching(
        this ObservedInvocations observed,
        Func<Observation, bool> predicate,
        int count,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        var deadline = DateTime.UtcNow + (timeout ?? ObservedInvocations.DefaultTimeout);

        while (true) {
            var matching = (await observed.Current(cancellationToken)).Where(predicate).ToArray();

            if (matching.Length >= count) {
                return matching;
            }

            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException(
                    $"Waited {(timeout ?? ObservedInvocations.DefaultTimeout).TotalSeconds:0} s for {count} " +
                    $"matching observation(s) and saw {matching.Length}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
