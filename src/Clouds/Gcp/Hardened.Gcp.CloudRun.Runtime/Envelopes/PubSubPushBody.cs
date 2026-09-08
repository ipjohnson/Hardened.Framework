using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// Reading a Pub/Sub message out of the JSON every push-shaped delivery carries it in.
/// </summary>
/// <remarks>
/// <para>
/// A push subscription posts <c>{"message":{...},"subscription":"..."}</c>; an Eventarc trigger
/// on a topic posts the same object as a CloudEvent's data, because a push subscription is what
/// Eventarc creates underneath; and a Cloud Storage notification through Pub/Sub is the same
/// object with the object's metadata as attributes. Three adapters in three packages read one
/// shape, so the reader lives here, the way the Lambda runtime keeps the <c>Records</c> reader
/// every record-array source needs.
/// </para>
/// <para>
/// <b>Reading is not recognising.</b> Nothing here says which adapter a push belongs to; each
/// asks about the attributes it needs after reading them.
/// </para>
/// </remarks>
public static class PubSubPushBody {
    /// <summary>The subscription's full resource name.</summary>
    public const string SubscriptionHeader = "x-goog-pubsub-subscription-name";

    /// <summary>The message id, so a handler can log or deduplicate on it.</summary>
    public const string MessageIdHeader = "x-goog-pubsub-message-id";

    /// <summary>When the message was published, as Pub/Sub wrote it.</summary>
    public const string PublishTimeHeader = "x-goog-pubsub-publish-time";

    /// <summary>The ordering key, when the message has one.</summary>
    public const string OrderingKeyHeader = "x-goog-pubsub-ordering-key";

    /// <summary>
    /// How many times Pub/Sub has tried to deliver the message, when the subscription reports it.
    /// Pub/Sub writes no header for this on an unwrapped push, so the name is this line's, under
    /// the same prefix as the four Pub/Sub itself writes.
    /// </summary>
    public const string DeliveryAttemptHeader = "x-goog-pubsub-delivery-attempt";

    private const string Message = "message";
    private const string Subscription = "subscription";
    private const string Data = "data";
    private const string Attributes = "attributes";
    private const string DeliveryAttempt = "deliveryAttempt";

    /// <summary>
    /// Whether <paramref name="root"/> is a push body: an object with a <c>message</c> object and
    /// a <c>subscription</c> string.
    /// </summary>
    public static bool TryRead(JsonElement root, out JsonElement message, out string subscription) {
        message = default;
        subscription = "";

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(Message, out message) ||
            message.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(Subscription, out var element) ||
            element.ValueKind != JsonValueKind.String) {
            return false;
        }

        subscription = element.GetString() ?? "";

        return true;
    }

    /// <summary>
    /// Writes the message's attributes and metadata onto <paramref name="headers"/> and returns its
    /// data, decoded, as the body.
    /// </summary>
    /// <remarks>
    /// The metadata is written after the attributes, so an attribute that happens to carry one of
    /// the metadata names cannot stand in for the metadata. The data is empty rather than null for
    /// a message that carried none, so a handler binding a body sees nothing to bind rather than a
    /// null reference.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The data is not base64. Raised rather than passed on as bytes: a push whose data is not
    /// base64 is a malformed delivery, and answering it 500 is what makes Pub/Sub redeliver and
    /// eventually dead-letter it, where a handler failing to bind garbage would look like the
    /// handler's fault.
    /// </exception>
    public static Stream Read(
        JsonElement message, string subscription, JsonElement root, IDictionary<string, StringValues> headers) {
        if (message.TryGetProperty(Attributes, out var attributes) && attributes.ValueKind == JsonValueKind.Object) {
            foreach (var attribute in attributes.EnumerateObject()) {
                if (attribute.Value.ValueKind == JsonValueKind.String) {
                    headers[attribute.Name] = attribute.Value.GetString();
                }
            }
        }

        Set(headers, MessageIdHeader, String(message, "messageId") ?? String(message, "message_id"));
        Set(headers, PublishTimeHeader, String(message, "publishTime") ?? String(message, "publish_time"));
        Set(headers, OrderingKeyHeader, String(message, "orderingKey"));
        Set(headers, SubscriptionHeader, subscription);

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(DeliveryAttempt, out var attempt) &&
            attempt.ValueKind == JsonValueKind.Number) {
            headers[DeliveryAttemptHeader] = attempt.GetRawText();
        }

        var data = String(message, Data);

        if (string.IsNullOrEmpty(data)) {
            return Stream.Null;
        }

        try {
            return new MemoryStream(Convert.FromBase64String(data!), writable: false);
        }
        catch (FormatException exception) {
            throw new InvalidOperationException(
                "The Pub/Sub message carried data that is not base64, which every message is.", exception);
        }
    }

    /// <summary>
    /// The subscription's own name, off the end of the resource name.
    /// </summary>
    /// <remarks>
    /// <c>projects/p/subscriptions/orders</c> ends in the name, which is the only part of it a
    /// handler declared. Anything that is not a resource name is returned whole, and an empty one
    /// gives an empty name - both produce a route no handler declared, which dispatch reports with
    /// the value in hand rather than failing inside the envelope.
    /// </remarks>
    public static string SubscriptionName(string subscription) {
        var slash = subscription.LastIndexOf('/');

        return slash > -1 ? subscription.Substring(slash + 1) : subscription;
    }

    /// <summary>A string property of a JSON object, or null when absent or not a string.</summary>
    public static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
