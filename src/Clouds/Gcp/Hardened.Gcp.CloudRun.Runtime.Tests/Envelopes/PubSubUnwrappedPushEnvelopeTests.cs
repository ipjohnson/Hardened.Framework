using Hardened.Gcp.CloudRun.PubSub;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A push with payload unwrapping and metadata writing on: the data is the body, everything else
/// is a header already.
/// </summary>
public class PubSubUnwrappedPushEnvelopeTests {
    private static readonly PubSubUnwrappedPushEnvelope Envelope = new();

    [Fact]
    public void TheSubscriptionHeaderRoutesAndTheBodyIsHandedOnAsItArrived() {
        var delivery = Deliveries.Post("/",
            (PubSubPushBody.SubscriptionHeader, "projects/p/subscriptions/orders"),
            (PubSubPushBody.MessageIdHeader, "9"),
            ("tenant", "acme"));

        var request = Deliveries.Unwrap(Envelope, delivery, "{\"id\":\"u-1\"}")!;

        Assert.Equal("QUEUE", request.Method);
        Assert.Equal("/orders", request.Path);
        Assert.Equal("{\"id\":\"u-1\"}", Deliveries.Text(request.Body));
        Assert.Equal("9", request.Headers[PubSubPushBody.MessageIdHeader].ToString());
        Assert.Equal("acme", request.Headers["tenant"].ToString());
        Assert.Equal("application/json", request.ContentType);
    }

    /// <summary>Without metadata writing there is nothing to recognise an unwrapped push by.</summary>
    [Fact]
    public void APostWithoutTheSubscriptionHeaderIsNotRecognised() {
        Assert.False(Envelope.Recognises(Deliveries.Post("/", ("tenant", "acme"))));
    }

    [Fact]
    public void OnlyAPostIsRecognised() {
        Assert.False(Envelope.Recognises(Deliveries.Request("GET", "/", null,
            (PubSubPushBody.SubscriptionHeader, "projects/p/subscriptions/orders"))));
    }
}
