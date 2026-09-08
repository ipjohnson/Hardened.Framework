using System.Text.Json;
using Hardened.CloudEvents;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.PubSub;

/// <summary>
/// A Pub/Sub message delivered through an Eventarc trigger on its topic: a CloudEvent of type
/// <c>google.cloud.pubsub.topic.v1.messagePublished</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is what serves <c>[Topic]</c>, per D5. A push subscription's body never says which topic
/// the message was published to; an Eventarc trigger does, in <c>ce-source</c>
/// (<c>//pubsub.googleapis.com/projects/p/topics/order-events</c>), and its data is the same push
/// body a subscription would have posted, because a push subscription is what Eventarc creates
/// underneath. So the message is read the way a push is, and the route is the topic's name off
/// the end of the source: <c>TOPIC /order-events</c>.
/// </para>
/// <para>
/// Both content modes. Eventarc sends binary mode - the push body under <c>ce-</c> headers - and
/// a structured-mode delivery carries the same body as the event's <c>data</c>. The CloudEvent's
/// attributes travel as the <c>ce-</c> headers beside the message's own, so a handler can read
/// the event id and the topic as well as the message id.
/// </para>
/// </remarks>
public sealed class PubSubTopicEnvelope : ITriggerEnvelope {
    /// <summary>The scheme a topic delivery routes under.</summary>
    public const string TopicScheme = "TOPIC";

    /// <summary>The CloudEvent type Eventarc gives a message published to a topic.</summary>
    public const string MessagePublishedType = "google.cloud.pubsub.topic.v1.messagePublished";

    /// <summary>
    /// A structured CloudEvent, whose type is inside the body, or a binary one whose type header
    /// says it is a message published.
    /// </summary>
    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        (CloudEventReader.IsStructured(request.ContentType) ||
         string.Equals(TriggerHeaders.Get(request.Headers, CloudEventHeaders.Type), MessagePublishedType, StringComparison.Ordinal));

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        CloudEvent cloudEvent;

        try {
            cloudEvent = CloudEventReader.Read(request.ContentType, request.Headers, payload.Raw);
        }
        catch (CloudEventFormatException) {
            // Not a CloudEvent after all - a structured content type over a body that is not one.
            // Declined, and whatever else recognised the request gets its turn.
            return null;
        }

        if (!string.Equals(cloudEvent.Type, MessagePublishedType, StringComparison.Ordinal)) {
            return null;
        }

        using var document = ParseData(cloudEvent);

        if (!PubSubPushBody.TryRead(document.RootElement, out var message, out var subscription)) {
            throw new InvalidOperationException(
                "The Eventarc delivery for a published message carries no message: its data is not " +
                "the push body Pub/Sub sends.");
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, cloudEvent);

        var body = PubSubPushBody.Read(message, subscription, document.RootElement, headers);

        return new CloudRunTriggerRequest(
            TopicScheme, "/" + CloudEventRoutes.LastSegment(cloudEvent.Source), body, headers, request);
    }

    private static JsonDocument ParseData(CloudEvent cloudEvent) {
        try {
            return JsonDocument.Parse(cloudEvent.Data);
        }
        catch (JsonException exception) {
            throw new InvalidOperationException(
                "The Eventarc delivery for a published message carries data that is not JSON.", exception);
        }
    }
}
