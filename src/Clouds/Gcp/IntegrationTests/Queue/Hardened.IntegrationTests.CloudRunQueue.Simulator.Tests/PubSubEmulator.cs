using DotNet.Testcontainers.Networks;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Testcontainers.PubSub;

namespace Hardened.IntegrationTests.CloudRunQueue.Simulator.Tests;

/// <summary>
/// The Pub/Sub emulator, and the two clients the tests drive it with.
/// </summary>
/// <remarks>
/// <para>
/// The emulator supports push subscriptions, and a push subscription whose endpoint is the
/// service's alias on the same network is what turns a publish into the HTTP request Cloud Run
/// would receive. The clients are Google's own, pointed at the emulator the way they are pointed
/// at one anywhere: <c>PUBSUB_EMULATOR_HOST</c> in the environment and emulator detection on the
/// builder, so the code that publishes here is the code that publishes in production.
/// </para>
/// <para>
/// The environment variable is process-wide, which is fine in a test assembly that exists for
/// this tier and wrong anywhere else.
/// </para>
/// </remarks>
public sealed class PubSubEmulator : IAsyncDisposable {
    public const string Alias = "pubsub";

    /// <summary>
    /// The Cloud SDK image with the emulators, pinned. Testcontainers.PubSub 4.14.0 defaults to
    /// the 446.0.1 tag, which gcr.io no longer serves; the registry keeps the last few dozen
    /// <c>-emulators</c> tags, so this one moves forward by hand when it goes the same way.
    /// </summary>
    public const string Image = "gcr.io/google.com/cloudsdktool/google-cloud-cli:583.0.0-emulators";

    /// <summary>The emulator accepts any project; this is the one every resource lives in.</summary>
    public const string ProjectId = "hardened-test";

    private readonly PubSubContainer _container;

    public PubSubEmulator(INetwork network) {
        _container = new PubSubBuilder(Image)
            .WithNetwork(network)
            .WithNetworkAliases(Alias)
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        await _container.StartAsync(cancellationToken);

        Environment.SetEnvironmentVariable(
            "PUBSUB_EMULATOR_HOST", new Uri(_container.GetEmulatorEndpoint()).Authority);
    }

    /// <summary>
    /// A topic with one push subscription delivering to <paramref name="pushEndpoint"/>.
    /// </summary>
    public async Task<TopicName> CreateTopicWithPushSubscription(
        string topic, string subscription, string pushEndpoint, CancellationToken cancellationToken = default) {
        var topicName = TopicName.FromProjectTopic(ProjectId, topic);

        await Publisher().CreateTopicAsync(topicName, cancellationToken);

        await Subscriber().CreateSubscriptionAsync(
            new Subscription {
                SubscriptionName = SubscriptionName.FromProjectSubscription(ProjectId, subscription),
                TopicAsTopicName = topicName,
                PushConfig = new PushConfig { PushEndpoint = pushEndpoint },
                AckDeadlineSeconds = 10
            },
            cancellationToken);

        return topicName;
    }

    /// <summary>Publishes one message with <paramref name="json"/> as its data.</summary>
    public Task PublishAsync(TopicName topic, string json, CancellationToken cancellationToken = default) =>
        Publisher().PublishAsync(
            topic, new[] { new PubsubMessage { Data = ByteString.CopyFromUtf8(json) } }, cancellationToken);

    private static PublisherServiceApiClient Publisher() =>
        new PublisherServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOrProduction }.Build();

    private static SubscriberServiceApiClient Subscriber() =>
        new SubscriberServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOrProduction }.Build();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
