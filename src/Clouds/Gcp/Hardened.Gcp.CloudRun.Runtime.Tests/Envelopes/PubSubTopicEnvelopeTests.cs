using Hardened.Gcp.CloudRun.PubSub;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A message on a topic, delivered by an Eventarc trigger as a CloudEvent whose data is the push
/// body.
/// </summary>
public class PubSubTopicEnvelopeTests {
    private static readonly PubSubTopicEnvelope Envelope = new();

    private const string Source = "//pubsub.googleapis.com/projects/p/topics/order-events";

    private static string Push(string data) => $$"""
        {"message":{"data":"{{Deliveries.Base64(data)}}","attributes":{"tenant":"acme"},
         "messageId":"7","message_id":"7","publishTime":"2026-09-07T10:00:00Z","publish_time":"2026-09-07T10:00:00Z"},
         "subscription":"projects/p/subscriptions/eventarc-order-events-sub-123"}
        """;

    [Fact]
    public void TheTopicOffTheSourceIsTheRoute() {
        var request = Deliveries.Unwrap(
            Envelope, Deliveries.CloudEvent(PubSubTopicEnvelope.MessagePublishedType, Source), Push("{\"id\":\"t-1\"}"))!;

        Assert.Equal("TOPIC", request.Method);
        Assert.Equal("/order-events", request.Path);
        Assert.Equal("{\"id\":\"t-1\"}", Deliveries.Text(request.Body));
    }

    /// <summary>The message's own headers and the event's both reach the handler.</summary>
    [Fact]
    public void TheMessageAndTheEventBothBecomeHeaders() {
        var request = Deliveries.Unwrap(
            Envelope, Deliveries.CloudEvent(PubSubTopicEnvelope.MessagePublishedType, Source, id: "evt-9"), Push("{}"))!;

        Assert.Equal("acme", request.Headers["tenant"].ToString());
        Assert.Equal("7", request.Headers[PubSubPushBody.MessageIdHeader].ToString());
        Assert.Equal("evt-9", request.Headers["ce-id"].ToString());
        Assert.Equal(Source, request.Headers["ce-source"].ToString());
        Assert.Equal(PubSubTopicEnvelope.MessagePublishedType, request.Headers["ce-type"].ToString());
    }

    [Fact]
    public void TheStructuredFormReadsTheSame() {
        var structured = $$"""
            {"specversion":"1.0","id":"evt-2","source":"{{Source}}","type":"{{PubSubTopicEnvelope.MessagePublishedType}}",
             "data":{{Push("{\"id\":\"t-2\"}")}}}
            """;

        var request = Deliveries.Unwrap(
            Envelope, Deliveries.Request("POST", "/", "application/cloudevents+json"), structured)!;

        Assert.Equal("/order-events", request.Path);
        Assert.Equal("{\"id\":\"t-2\"}", Deliveries.Text(request.Body));
    }

    /// <summary>A structured CloudEvent of another type is declined, so the adapter that owns it can take it.</summary>
    [Fact]
    public void AnotherTypeIsDeclined() {
        var request = Deliveries.Request("POST", "/", "application/cloudevents+json");

        Assert.True(Envelope.Recognises(request));
        Assert.Null(Deliveries.Unwrap(Envelope, request,
            """{"specversion":"1.0","id":"1","source":"/s","type":"google.cloud.storage.object.v1.finalized","data":{}}"""));
    }

    [Fact]
    public void AStructuredContentTypeOverSomethingElseIsDeclined() {
        Assert.Null(Deliveries.Unwrap(Envelope, Deliveries.Request("POST", "/", "application/cloudevents+json"), "not a cloudevent"));
    }

    /// <summary>A plain push is a queue's, not a topic's.</summary>
    [Fact]
    public void APlainPushIsNotRecognised() {
        Assert.False(Envelope.Recognises(Deliveries.Post()));
    }

    [Fact]
    public void ABinaryEventOfAnotherTypeIsNotRecognised() {
        Assert.False(Envelope.Recognises(Deliveries.CloudEvent("google.cloud.storage.object.v1.finalized", "/s")));
    }

    [Fact]
    public void AMessagePublishedWithoutAPushBodyIsRefused() {
        Assert.Throws<InvalidOperationException>(() => Deliveries.Unwrap(
            Envelope, Deliveries.CloudEvent(PubSubTopicEnvelope.MessagePublishedType, Source), """{"not":"a push"}"""));
    }
}
