using Azure.Messaging.EventHubs;
using Hardened.Azure.Functions.EventHubs;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

public class EventHubsRequestTests {
    private static readonly EventHubsAdapter Adapter = new();

    private static EventHubsRequest Request(params EventData[] events) =>
        (EventHubsRequest)Adapter.CreateRequest(
            new FunctionsTrigger("STREAM", "/clickstream", events),
            new TestFunctionContext("Stream_clickstream", new Dictionary<string, object?>(), new ServiceCollection().BuildServiceProvider()));

    /// <summary>
    /// The facts the hub carries outside the body, under prefixed names, from an event built the
    /// way a test builds one.
    /// </summary>
    [Fact]
    public void AForkCarriesTheEventsPositionAndProperties() {
        var enqueued = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var eventData = EventHubsModelFactory.EventData(
            eventBody: BinaryData.FromString("""{"id":"c-1"}"""),
            properties: new Dictionary<string, object> { ["x-trace"] = "abc", ["attempt"] = 2 },
            partitionKey: "clicks",
            sequenceNumber: 7,
            offset: 640,
            enqueuedTime: enqueued);

        eventData.ContentType = "application/json";
        eventData.MessageId = "m-7";

        var fork = Request(eventData).ForItem(0);

        Assert.Equal("STREAM", fork.Method);
        Assert.Equal("/clickstream", fork.Path);
        Assert.Equal("7", fork.Headers[EventHubsRequest.SequenceNumberHeader].ToString());
        Assert.Equal("640", fork.Headers[EventHubsRequest.OffsetHeader].ToString());
        Assert.Equal("clicks", fork.Headers[EventHubsRequest.PartitionKeyHeader].ToString());
        Assert.Equal("2026-09-08T12:00:00.0000000+00:00", fork.Headers[EventHubsRequest.EnqueuedTimeHeader].ToString());
        Assert.Equal("m-7", fork.Headers[EventHubsRequest.MessageIdHeader].ToString());
        Assert.Equal("application/json", fork.ContentType);
        Assert.Equal("abc", fork.Headers["x-trace"].ToString());
        Assert.Equal("2", fork.Headers["attempt"].ToString());
        Assert.Equal("""{"id":"c-1"}""", new StreamReader(fork.Body).ReadToEnd());
    }

    /// <summary>
    /// The offset as the service wrote it. The worker's converter carries the host's text in the
    /// event's system properties, and an emulator or a geo-replicated namespace writes offsets no
    /// long can hold - which is where the SDK's own accessor threw under the real host.
    /// </summary>
    [Fact]
    public void AnOffsetTheServiceWroteAsTextIsCarriedAsIs() {
        var eventData = EventHubsModelFactory.EventData(
            eventBody: BinaryData.FromString("{}"),
            systemProperties: new Dictionary<string, object> {
                ["x-opt-offset"] = "0-128",
                ["x-opt-sequence-number"] = 1L,
                ["x-opt-enqueued-time"] = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc)
            });

        var fork = Request(eventData).ForItem(0);

        Assert.Equal("0-128", fork.Headers[EventHubsRequest.OffsetHeader].ToString());
        Assert.Equal("2026-09-08T12:00:00.0000000+00:00", fork.Headers[EventHubsRequest.EnqueuedTimeHeader].ToString());
    }

    [Fact]
    public void AnEventWithoutABodyHasNoBody() {
        var fork = Request(EventHubsModelFactory.EventData(eventBody: new BinaryData(Array.Empty<byte>()))).ForItem(0);

        Assert.Same(Stream.Null, fork.Body);
    }
}
