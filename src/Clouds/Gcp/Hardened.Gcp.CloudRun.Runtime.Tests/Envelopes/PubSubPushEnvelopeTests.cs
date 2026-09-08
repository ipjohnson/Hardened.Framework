using System.Text;
using Hardened.Gcp.CloudRun.PubSub;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// The push body, as Pub/Sub documents it, read into the request a handler meets.
/// </summary>
public class PubSubPushEnvelopeTests {
    private static readonly PubSubPushEnvelope Envelope = new();

    private const string Full = """
        {
          "message": {
            "data": "eyJpZCI6ImEtMSJ9",
            "attributes": {"tenant": "acme", "Content-Type": "application/json"},
            "messageId": "2070443601311540",
            "message_id": "2070443601311540",
            "publishTime": "2021-02-26T19:13:55.749Z",
            "publish_time": "2021-02-26T19:13:55.749Z",
            "orderingKey": "customer-7"
          },
          "subscription": "projects/myproject/subscriptions/orders",
          "deliveryAttempt": 5
        }
        """;

    private static TestExecutionRequest Post(string contentType = "application/json", string method = "POST") =>
        new(method, "/", null, EmptyQueryStringCollection.Instance) {
            Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
                ["Content-Type"] = contentType
            }
        };

    private static CloudRunTriggerRequest? Unwrap(string json) {
        using var payload = new TriggerPayload(Encoding.UTF8.GetBytes(json));

        return Envelope.Unwrap(Post(), payload);
    }

    [Fact]
    public void TheSubscriptionsLastSegmentIsTheRoute() {
        var request = Unwrap(Full)!;

        Assert.Equal("QUEUE", request.Method);
        Assert.Equal("/orders", request.Path);
    }

    [Fact]
    public void TheDataIsDecodedIntoTheBody() {
        var request = Unwrap(Full)!;

        using var reader = new StreamReader(request.Body);

        Assert.Equal("{\"id\":\"a-1\"}", reader.ReadToEnd());
    }

    [Fact]
    public void AttributesBecomeHeaders() {
        var request = Unwrap(Full)!;

        Assert.Equal("acme", request.Headers["tenant"].ToString());
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public void TheMetadataGetsPrefixedHeaders() {
        var request = Unwrap(Full)!;

        Assert.Equal("2070443601311540", request.Headers[PubSubPushEnvelope.MessageIdHeader].ToString());
        Assert.Equal("2021-02-26T19:13:55.749Z", request.Headers[PubSubPushEnvelope.PublishTimeHeader].ToString());
        Assert.Equal("customer-7", request.Headers[PubSubPushEnvelope.OrderingKeyHeader].ToString());
        Assert.Equal("projects/myproject/subscriptions/orders", request.Headers[PubSubPushEnvelope.SubscriptionHeader].ToString());
        Assert.Equal("5", request.Headers[PubSubPushEnvelope.DeliveryAttemptHeader].ToString());
    }

    /// <summary>The names Pub/Sub itself writes on an unwrapped push with metadata, so a handler reads one name for both forms.</summary>
    [Fact]
    public void TheMetadataHeadersAreTheOnesPubSubWritesOnAnUnwrappedPush() {
        Assert.Equal("x-goog-pubsub-message-id", PubSubPushEnvelope.MessageIdHeader);
        Assert.Equal("x-goog-pubsub-publish-time", PubSubPushEnvelope.PublishTimeHeader);
        Assert.Equal("x-goog-pubsub-ordering-key", PubSubPushEnvelope.OrderingKeyHeader);
        Assert.Equal("x-goog-pubsub-subscription-name", PubSubPushEnvelope.SubscriptionHeader);
    }

    /// <summary>An attribute cannot stand in for the metadata: the metadata is written after the attributes.</summary>
    [Fact]
    public void AnAttributeNamedLikeMetadataLosesToTheMetadata() {
        var request = Unwrap("""
            {"message":{"data":"","attributes":{"x-goog-pubsub-message-id":"forged"},"messageId":"real"},
             "subscription":"projects/p/subscriptions/orders"}
            """)!;

        Assert.Equal("real", request.Headers[PubSubPushEnvelope.MessageIdHeader].ToString());
    }

    [Fact]
    public void SnakeCaseMetadataIsReadWhenCamelCaseIsAbsent() {
        var request = Unwrap("""
            {"message":{"data":"","message_id":"snake","publish_time":"2021-02-26T19:13:55.749Z"},
             "subscription":"projects/p/subscriptions/orders"}
            """)!;

        Assert.Equal("snake", request.Headers[PubSubPushEnvelope.MessageIdHeader].ToString());
        Assert.Equal("2021-02-26T19:13:55.749Z", request.Headers[PubSubPushEnvelope.PublishTimeHeader].ToString());
    }

    [Fact]
    public void AMessageWithoutDataHasAnEmptyBody() {
        var request = Unwrap("""{"message":{"messageId":"1"},"subscription":"projects/p/subscriptions/orders"}""")!;

        Assert.Equal(0, request.Body.Length);
        Assert.False(request.Headers.ContainsKey(PubSubPushEnvelope.DeliveryAttemptHeader));
    }

    [Fact]
    public void DataThatIsNotBase64IsRefused() {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Unwrap("""{"message":{"data":"not base64!"},"subscription":"projects/p/subscriptions/orders"}"""));

        Assert.Contains("base64", failure.Message);
    }

    /// <summary>The delivery is kept, which is where a push's own headers - its authorization token - are.</summary>
    [Fact]
    public void TheDeliveryIsKeptOnTheRequest() {
        var delivery = Post();

        using var payload = new TriggerPayload(Encoding.UTF8.GetBytes(Full));

        var request = Envelope.Unwrap(delivery, payload)!;

        Assert.Same(delivery, request.Delivery);
    }

    [Theory]
    [InlineData("""{"subscription":"projects/p/subscriptions/orders"}""")]
    [InlineData("""{"message":"not an object","subscription":"projects/p/subscriptions/orders"}""")]
    [InlineData("""{"message":{"data":""}}""")]
    [InlineData("""{"message":{"data":""},"subscription":7}""")]
    [InlineData("""["a","push","this","is","not"]""")]
    [InlineData("not json at all")]
    public void AJsonPostThatIsNotAPushIsDeclined(string body) {
        Assert.Null(Unwrap(body));
    }

    [Theory]
    [InlineData("POST", "application/json", true)]
    [InlineData("post", "application/json; charset=utf-8", true)]
    [InlineData("POST", "text/plain", false)]
    [InlineData("POST", null, false)]
    [InlineData("GET", "application/json", false)]
    [InlineData("QUEUE", "application/json", false)]
    public void OnlyAJsonPostIsRecognised(string method, string? contentType, bool recognised) {
        var request = new TestExecutionRequest(method, "/", null, EmptyQueryStringCollection.Instance) {
            Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        };

        if (contentType != null) {
            request.Headers["Content-Type"] = contentType;
        }

        Assert.Equal(recognised, Envelope.Recognises(request));
    }

    [Theory]
    [InlineData("projects/p/subscriptions/orders", "orders")]
    [InlineData("orders", "orders")]
    [InlineData("", "")]
    public void TheSubscriptionNameIsItsLastSegment(string subscription, string name) {
        Assert.Equal(name, PubSubPushEnvelope.SubscriptionName(subscription));
    }
}
