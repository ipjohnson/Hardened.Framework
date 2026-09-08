using Hardened.CloudEvents;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Eventarc;

/// <summary>
/// Any CloudEvent Eventarc delivers, routed on its source and type.
/// </summary>
/// <remarks>
/// <para>
/// Eventarc delivers every event as a CloudEvents 1.0 HTTP POST, binary mode by default: the
/// attributes in <c>ce-</c> headers and the payload as the body. Structured mode, one JSON object
/// with everything in it, is read the same way through <c>Hardened.CloudEvents</c>. The event's
/// data becomes the handler's body under the data content type, and every attribute becomes a
/// <c>ce-</c> header, so a handler reads the same names an Azure Event Grid handler reads.
/// </para>
/// <para>
/// Routes as <c>EVENT /{source}/{type}</c>, the shape <c>[Event(source, type)]</c> declares and the
/// EventBridge adapter routes a bus event under. A source is a URI reference and keeps its
/// slashes, so a handler names it whole:
/// <c>[Event("//pubsub.googleapis.com/projects/p/topics/t", "google.cloud.pubsub.topic.v1.messagePublished")]</c>.
/// </para>
/// <para>
/// A fallback, so an adapter with a more specific reading of a type - a Storage object, a
/// Firestore document, a message on a topic - is asked first, and this serves what none of them
/// claimed.
/// </para>
/// </remarks>
public sealed class EventarcEnvelope : IFallbackTriggerEnvelope {
    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        (CloudEventReader.IsStructured(request.ContentType) || CloudEventReader.IsBinary(request.Headers));

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        CloudEvent cloudEvent;

        try {
            cloudEvent = CloudEventReader.Read(request.ContentType, request.Headers, payload.Raw);
        }
        catch (CloudEventFormatException exception) {
            // Raised rather than declined: the request said it was a CloudEvent and is not a
            // well-formed one, and a 500 names that where a 404 from the routing table would not.
            throw new InvalidOperationException(
                "Eventarc delivered a request that claims to be a CloudEvent and is not one: " + exception.Message,
                exception);
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, cloudEvent);

        if (!string.IsNullOrEmpty(cloudEvent.DataContentType)) {
            headers[KnownHeaders.ContentType] = cloudEvent.DataContentType;
        }

        return new CloudRunTriggerRequest(
            CloudEventRoutes.EventScheme,
            CloudEventRoutes.Event(cloudEvent),
            TriggerPayload.AsStream(cloudEvent.Data),
            headers,
            request);
    }
}
