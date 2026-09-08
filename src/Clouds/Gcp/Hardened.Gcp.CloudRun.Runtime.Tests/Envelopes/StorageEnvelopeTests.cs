using System.Text.Json;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Storage;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// An object change in both forms: the Eventarc CloudEvent and the Pub/Sub notification.
/// </summary>
public class StorageEnvelopeTests {
    private static readonly StorageEnvelope Envelope = new();

    private const string Metadata = """
        {"kind":"storage#object","id":"uploads/report.pdf/1588778055917163","bucket":"uploads","name":"report.pdf",
         "size":"1024","contentType":"application/pdf","generation":"1588778055917163","metageneration":"1",
         "etag":"COu8mb3Dn+kCEAE=","timeCreated":"2026-09-07T10:00:00.000Z","updated":"2026-09-07T10:00:00.000Z"}
        """;

    private static JsonElement Body(Stream body) {
        using var document = JsonDocument.Parse(Deliveries.Text(body));

        return document.RootElement.Clone();
    }

    [Fact]
    public void AnEventarcObjectEventRoutesOnTheBucketAndBindsTheNotification() {
        var delivery = Deliveries.CloudEvent(
            StorageEnvelope.ObjectTypePrefix + "finalized",
            "//storage.googleapis.com/projects/_/buckets/uploads",
            "objects/report.pdf");

        var request = Deliveries.Unwrap(Envelope, delivery, Metadata)!;

        Assert.Equal("BLOB", request.Method);
        Assert.Equal("/uploads", request.Path);

        var body = Body(request.Body);

        Assert.Equal("uploads", body.GetProperty("bucket").GetString());
        Assert.Equal("report.pdf", body.GetProperty("name").GetString());
        Assert.Equal(1024, body.GetProperty("size").GetInt64());
        Assert.Equal("application/pdf", body.GetProperty("contentType").GetString());
        Assert.Equal("1588778055917163", body.GetProperty("generation").GetString());
        Assert.Equal("OBJECT_FINALIZE", body.GetProperty("eventType").GetString());
        Assert.Equal("2026-09-07T10:00:00Z", body.GetProperty("eventTime").GetString());
    }

    /// <summary>Both forms carry the notification's attribute names, so a handler reads one set.</summary>
    [Fact]
    public void AnEventarcObjectEventCarriesTheNotificationAttributesAsHeaders() {
        var delivery = Deliveries.CloudEvent(
            StorageEnvelope.ObjectTypePrefix + "deleted",
            "//storage.googleapis.com/projects/_/buckets/uploads",
            "objects/report.pdf");

        var request = Deliveries.Unwrap(Envelope, delivery, Metadata)!;

        Assert.Equal("OBJECT_DELETE", request.Headers[StorageEnvelope.EventTypeHeader].ToString());
        Assert.Equal("uploads", request.Headers[StorageEnvelope.BucketHeader].ToString());
        Assert.Equal("report.pdf", request.Headers[StorageEnvelope.ObjectHeader].ToString());
        Assert.Equal("1588778055917163", request.Headers[StorageEnvelope.GenerationHeader].ToString());
        Assert.Equal(StorageEnvelope.ObjectTypePrefix + "deleted", request.Headers["ce-type"].ToString());
    }

    /// <summary>The published examples write size as a number; Eventarc writes it as a string. Both read.</summary>
    [Fact]
    public void ASizeWrittenAsANumberReadsTheSame() {
        var delivery = Deliveries.CloudEvent(
            StorageEnvelope.ObjectTypePrefix + "finalized", "//storage.googleapis.com/projects/_/buckets/uploads");

        var request = Deliveries.Unwrap(Envelope, delivery, """{"bucket":"uploads","name":"a.txt","size":12,"generation":7}""")!;

        var body = Body(request.Body);

        Assert.Equal(12, body.GetProperty("size").GetInt64());
        Assert.Equal("7", body.GetProperty("generation").GetString());
    }

    [Fact]
    public void ANotificationThroughPubSubRoutesOnTheBucketAndBindsTheSameNotification() {
        var push = $$"""
            {"message":{"data":"{{Deliveries.Base64(Metadata)}}",
              "attributes":{"eventType":"OBJECT_FINALIZE","bucketId":"uploads","objectId":"report.pdf",
                            "objectGeneration":"1588778055917163","eventTime":"2026-09-07T10:00:00.000Z",
                            "payloadFormat":"JSON_API_V1","notificationConfig":"projects/_/buckets/uploads/notificationConfigs/1"},
              "messageId":"5","publishTime":"2026-09-07T10:00:01Z"},
             "subscription":"projects/p/subscriptions/uploads-notifications"}
            """;

        var request = Deliveries.Unwrap(Envelope, Deliveries.Post(), push)!;

        Assert.Equal("BLOB", request.Method);
        Assert.Equal("/uploads", request.Path);

        var body = Body(request.Body);

        Assert.Equal("report.pdf", body.GetProperty("name").GetString());
        Assert.Equal(1024, body.GetProperty("size").GetInt64());
        Assert.Equal("OBJECT_FINALIZE", body.GetProperty("eventType").GetString());
        Assert.Equal("2026-09-07T10:00:00.000Z", body.GetProperty("eventTime").GetString());
        Assert.Equal("uploads", request.Headers[StorageEnvelope.BucketHeader].ToString());
        Assert.Equal("5", request.Headers[PubSubPushBody.MessageIdHeader].ToString());
    }

    /// <summary>A notification configured with no payload still names the object through its attributes.</summary>
    [Fact]
    public void ANotificationWithNoPayloadBindsWhatTheAttributesSay() {
        const string push = """
            {"message":{"attributes":{"eventType":"OBJECT_DELETE","bucketId":"uploads","objectId":"old.txt",
                                      "objectGeneration":"3","payloadFormat":"NONE"},"messageId":"6"},
             "subscription":"projects/p/subscriptions/uploads-notifications"}
            """;

        var request = Deliveries.Unwrap(Envelope, Deliveries.Post(), push)!;

        var body = Body(request.Body);

        Assert.Equal("uploads", body.GetProperty("bucket").GetString());
        Assert.Equal("old.txt", body.GetProperty("name").GetString());
        Assert.Equal("3", body.GetProperty("generation").GetString());
        Assert.Equal("OBJECT_DELETE", body.GetProperty("eventType").GetString());
        Assert.Equal(JsonValueKind.Undefined, body.TryGetProperty("size", out var size) ? size.ValueKind : JsonValueKind.Undefined);
    }

    /// <summary>A push that is not a notification is a queue's, and is declined for the push envelope to take.</summary>
    [Fact]
    public void APlainPushIsDeclined() {
        Assert.True(Envelope.Recognises(Deliveries.Post()));
        Assert.Null(Deliveries.Unwrap(Envelope, Deliveries.Post(),
            """{"message":{"data":"e30=","attributes":{"tenant":"acme"}},"subscription":"projects/p/subscriptions/orders"}"""));
    }

    [Fact]
    public void ACloudEventOfAnotherTypeIsDeclined() {
        Assert.False(Envelope.Recognises(Deliveries.CloudEvent("google.cloud.pubsub.topic.v1.messagePublished", "/s")));
        Assert.Null(Deliveries.Unwrap(Envelope, Deliveries.Request("POST", "/", "application/cloudevents+json"),
            """{"specversion":"1.0","id":"1","source":"/s","type":"google.cloud.pubsub.topic.v1.messagePublished","data":{}}"""));
    }

    [Theory]
    [InlineData("finalized", "OBJECT_FINALIZE")]
    [InlineData("deleted", "OBJECT_DELETE")]
    [InlineData("archived", "OBJECT_ARCHIVE")]
    [InlineData("metadataUpdated", "OBJECT_METADATA_UPDATE")]
    [InlineData("somethingNew", "somethingNew")]
    public void TheEventIsNamedTheNotificationsWay(string suffix, string eventType) {
        Assert.Equal(eventType, StorageEnvelope.NotificationEventType(suffix));
    }
}
