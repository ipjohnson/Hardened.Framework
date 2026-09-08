using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Google.Cloud.PubSub.V1;
using Hardened.Gcp.CloudRun.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunBlob.Simulator.Tests;

/// <summary>
/// The blob fixture in the image Cloud Run runs, driven by a Cloud Storage notification through
/// the Pub/Sub emulator.
/// </summary>
/// <remarks>
/// <para>
/// The notification form rather than the Eventarc one, because it is the form an emulator can
/// deliver: a push subscription on the topic a bucket's notification configuration would publish
/// to, with the message carrying the attributes Cloud Storage writes and the object's metadata as
/// its data. The message is published by hand, which is the matrix's fallback for this row;
/// fake-gcs-server publishing it is the best-effort path, whose notification is reported
/// unreliable, and it is not attempted here.
/// </para>
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class StorageNotificationTests : IClassFixture<StorageNotificationTests.Stack> {
    private readonly Stack _stack;

    public StorageNotificationTests(Stack stack) {
        _stack = stack;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStorageNotificationThroughTheEmulatorReachesTheHandlerInTheContainer() {
        await _stack.Emulator.PublishAsync(
            _stack.Topic,
            """{"kind":"storage#object","bucket":"uploads","name":"report.pdf","size":"1024","contentType":"application/pdf","generation":"7"}""",
            new Dictionary<string, string> {
                ["eventType"] = "OBJECT_FINALIZE",
                ["bucketId"] = "uploads",
                ["objectId"] = "report.pdf",
                ["objectGeneration"] = "7",
                ["eventTime"] = "2026-09-07T10:00:00.000Z",
                ["payloadFormat"] = "JSON_API_V1",
                ["notificationConfig"] = "projects/_/buckets/uploads/notificationConfigs/1"
            },
            Token);

        var observed = await _stack.Service.Observed.WaitFor(one => one.Has("name", "report.pdf"), cancellationToken: Token);

        Assert.Equal("blob", observed.Get("kind"));
        Assert.Equal("uploads", observed.Get("bucket"));
        Assert.Equal("OBJECT_FINALIZE", observed.Get("eventType"));
        Assert.Equal(1024, observed.Fields.GetProperty("size").GetInt64());
    }

    public sealed class Stack : IAsyncLifetime {
        private readonly INetwork _network = new NetworkBuilder().Build();

        public Stack() {
            Service = CloudRunService.For("Hardened.IntegrationTests.CloudRunBlob.SUT", _network);
            Emulator = new PubSubEmulator(_network);
        }

        public CloudRunService Service { get; }

        public PubSubEmulator Emulator { get; }

        public TopicName Topic { get; private set; } = null!;

        public async ValueTask InitializeAsync() {
            await _network.CreateAsync(Token);

            await Task.WhenAll(Service.StartAsync(Token), Emulator.StartAsync(Token));

            // The topic a bucket's notification configuration would publish to; the subscription's
            // name is the deployment's and the handler never sees it, because a notification
            // routes on the bucket.
            Topic = await Emulator.CreateTopicWithPushSubscription(
                "uploads-notifications", "uploads-to-service", Service.PushEndpoint, Token);
        }

        public async ValueTask DisposeAsync() {
            await Emulator.DisposeAsync();
            await Service.DisposeAsync();
            await _network.DisposeAsync();
        }
    }
}
