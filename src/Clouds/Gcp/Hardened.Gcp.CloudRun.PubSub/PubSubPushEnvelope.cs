using Hardened.CloudEvents;
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
/// A fallback, because a push is any JSON POST with a message in it and a Cloud Storage
/// notification is exactly that with two attributes more. And never a CloudEvent: an Eventarc
/// trigger on a topic posts the same body under <c>ce-</c> headers, and that delivery is a topic's,
/// so a request the CloudEvent readers recognise is declined here before its body is read.
/// </para>
/// <para>
/// One message per request, never a batch, and no acknowledgement to write: Pub/Sub reads the
/// status. A handler that throws is answered 500 by Kestrel, which is a negative acknowledgement,
/// and the message is redelivered.
/// </para>
/// </remarks>
public sealed class PubSubPushEnvelope : IFallbackTriggerEnvelope {
    /// <summary>The scheme a push routes under.</summary>
    public const string QueueScheme = "QUEUE";

    /// <summary>The subscription's full resource name.</summary>
    public const string SubscriptionHeader = PubSubPushBody.SubscriptionHeader;

    /// <summary>The message id, so a handler can log or deduplicate on it.</summary>
    public const string MessageIdHeader = PubSubPushBody.MessageIdHeader;

    /// <summary>When the message was published, as Pub/Sub wrote it.</summary>
    public const string PublishTimeHeader = PubSubPushBody.PublishTimeHeader;

    /// <summary>The ordering key, when the message has one.</summary>
    public const string OrderingKeyHeader = PubSubPushBody.OrderingKeyHeader;

    /// <summary>How many times Pub/Sub has tried to deliver the message, when the subscription reports it.</summary>
    public const string DeliveryAttemptHeader = PubSubPushBody.DeliveryAttemptHeader;

    /// <summary>A JSON POST that is not a CloudEvent. Everything else on the service is not a push.</summary>
    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        IsJson(request.ContentType) &&
        !CloudEventReader.IsBinary(request.Headers);

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        if (payload.Json is not { } root || !PubSubPushBody.TryRead(root, out var message, out var subscription)) {
            return null;
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var body = PubSubPushBody.Read(message, subscription, root, headers);

        return new CloudRunTriggerRequest(
            QueueScheme, "/" + PubSubPushBody.SubscriptionName(subscription), body, headers, request);
    }

    /// <summary>The subscription's own name, off the end of the resource name.</summary>
    internal static string SubscriptionName(string subscription) => PubSubPushBody.SubscriptionName(subscription);

    internal static bool IsJson(string? contentType) =>
        contentType != null &&
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
}
