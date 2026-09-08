using System.Text.Json;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.PubSub;

/// <summary>
/// A Pub/Sub push subscription's delivery, wrapped: a JSON POST carrying the message and the
/// subscription that delivered it.
/// </summary>
/// <remarks>
/// <para>
/// The body Pub/Sub sends is documented and small:
/// <c>{"message":{"data":"&lt;base64&gt;","attributes":{...},"messageId":"...","publishTime":"...",
/// "orderingKey":"..."},"subscription":"projects/p/subscriptions/orders","deliveryAttempt":5}</c>,
/// with <c>message_id</c> and <c>publish_time</c> sent beside their camel-case twins. The decoded
/// data is the handler's body, the attributes are its headers under their own names, and the
/// metadata gets prefixed headers - the same names Pub/Sub writes on an unwrapped push with
/// metadata enabled, so a handler reads one name whichever form the subscription uses.
/// </para>
/// <para>
/// Routes as <c>QUEUE /orders</c>, taking the name off the end of the subscription rather than
/// from the handler's attribute. The attribute says which queue a handler wants; the envelope says
/// which subscription actually delivered, and routing on the latter is what makes a push endpoint
/// wired to the wrong subscription a missing route rather than a message handled by the wrong
/// code. A subscription rather than a topic, per D5: the push body does not carry the topic, and a
/// subscription with competing consumers is what a queue is.
/// </para>
/// <para>
/// One message per request, never a batch, and no acknowledgement to write: Pub/Sub reads the
/// status. A handler that throws is answered 500 by Kestrel, which is a negative acknowledgement,
/// and the message is redelivered.
/// </para>
/// </remarks>
public sealed class PubSubPushEnvelope : ITriggerEnvelope {
    /// <summary>The scheme a push routes under.</summary>
    public const string QueueScheme = "QUEUE";

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
    /// Pub/Sub writes no header for this on an unwrapped push, so the name is this package's,
    /// under the same prefix.
    /// </summary>
    public const string DeliveryAttemptHeader = "x-goog-pubsub-delivery-attempt";

    private const string Message = "message";
    private const string Subscription = "subscription";
    private const string Data = "data";
    private const string Attributes = "attributes";
    private const string DeliveryAttempt = "deliveryAttempt";

    /// <summary>A JSON POST. Everything else on the service is not a push.</summary>
    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        IsJson(request.ContentType);

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        if (payload.Json is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty(Message, out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(Subscription, out var subscription) ||
            subscription.ValueKind != JsonValueKind.String) {
            return null;
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (message.TryGetProperty(Attributes, out var attributes) && attributes.ValueKind == JsonValueKind.Object) {
            foreach (var attribute in attributes.EnumerateObject()) {
                if (attribute.Value.ValueKind == JsonValueKind.String) {
                    headers[attribute.Name] = attribute.Value.GetString();
                }
            }
        }

        var subscriptionName = subscription.GetString() ?? "";

        // After the attributes, so an attribute that happens to carry one of these names cannot
        // stand in for the metadata.
        Set(headers, MessageIdHeader, String(message, "messageId") ?? String(message, "message_id"));
        Set(headers, PublishTimeHeader, String(message, "publishTime") ?? String(message, "publish_time"));
        Set(headers, OrderingKeyHeader, String(message, "orderingKey"));
        Set(headers, SubscriptionHeader, subscriptionName);

        if (root.TryGetProperty(DeliveryAttempt, out var attempt) && attempt.ValueKind == JsonValueKind.Number) {
            headers[DeliveryAttemptHeader] = attempt.GetRawText();
        }

        return new CloudRunTriggerRequest(
            QueueScheme, "/" + SubscriptionName(subscriptionName), Body(message), headers, request);
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
    internal static string SubscriptionName(string subscription) {
        var slash = subscription.LastIndexOf('/');

        return slash > -1 ? subscription.Substring(slash + 1) : subscription;
    }

    /// <summary>
    /// The message's data, decoded. Empty rather than null for a message that carried none, so a
    /// handler binding a body sees nothing to bind rather than a null reference.
    /// </summary>
    private static Stream Body(JsonElement message) {
        var data = String(message, Data);

        if (string.IsNullOrEmpty(data)) {
            return Stream.Null;
        }

        try {
            return new MemoryStream(Convert.FromBase64String(data!), writable: false);
        }
        catch (FormatException exception) {
            // Raised rather than passed on as bytes: a push whose data is not base64 is a
            // malformed delivery, and answering it 500 is what makes Pub/Sub redeliver and
            // eventually dead-letter it, where a handler failing to bind garbage would look like
            // the handler's fault.
            throw new InvalidOperationException(
                "The Pub/Sub push carried message.data that is not base64, which every push is.",
                exception);
        }
    }

    private static bool IsJson(string? contentType) =>
        contentType != null &&
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
