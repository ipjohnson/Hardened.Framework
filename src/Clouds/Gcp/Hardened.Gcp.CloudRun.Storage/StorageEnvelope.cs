using System.Text.Json;
using Hardened.CloudEvents;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Storage;

/// <summary>
/// A Cloud Storage object change, in either of the two forms it is delivered in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two forms, both production paths.</b> An Eventarc trigger on a bucket delivers a CloudEvent
/// of type <c>google.cloud.storage.object.v1.finalized</c>, <c>deleted</c>, <c>archived</c> or
/// <c>metadataUpdated</c>, with the object's metadata as JSON data, the bucket in
/// <c>ce-source</c> and the object in <c>ce-subject</c>. A Pub/Sub notification configured on the
/// bucket arrives as a push whose message attributes carry <c>eventType</c>, <c>bucketId</c>,
/// <c>objectId</c>, <c>objectGeneration</c> and <c>eventTime</c>, and whose data is the same
/// metadata in the JSON API's form, or nothing. The second is the form the emulator tier can
/// drive; a handler cannot tell them apart, and that is the point.
/// </para>
/// <para>
/// Routes as <c>BLOB /uploads</c>, the bucket taken from the delivery rather than the attribute.
/// The body is <see cref="StorageNotification"/>: the metadata flattened, with the event named the
/// notification's way (<c>OBJECT_FINALIZE</c> for <c>finalized</c>, and so on) whichever form it
/// came in. The headers are the notification's attribute names in both forms, synthesised for the
/// CloudEvent one, plus the <c>ce-</c> attributes when there was an event.
/// </para>
/// <para>
/// Not a fallback, and it recognises every JSON POST for the notification form's sake: it is
/// asked before the plain push envelope and declines the pushes that carry no <c>eventType</c>
/// and <c>bucketId</c>, which then go on to be a queue's.
/// </para>
/// </remarks>
public sealed class StorageEnvelope : ITriggerEnvelope {
    /// <summary>The scheme an object store routes under, which <c>[Blob]</c> declares.</summary>
    public const string BlobScheme = "BLOB";

    /// <summary>What every object CloudEvent's type starts with.</summary>
    public const string ObjectTypePrefix = "google.cloud.storage.object.v1.";

    /// <summary>The notification attribute, and header, naming the event.</summary>
    public const string EventTypeHeader = "eventType";

    /// <summary>The notification attribute, and header, naming the bucket.</summary>
    public const string BucketHeader = "bucketId";

    /// <summary>The notification attribute, and header, naming the object.</summary>
    public const string ObjectHeader = "objectId";

    /// <summary>The notification attribute, and header, carrying the object's generation.</summary>
    public const string GenerationHeader = "objectGeneration";

    /// <summary>The notification attribute, and header, carrying when the change happened.</summary>
    public const string EventTimeHeader = "eventTime";

    public bool Recognises(IExecutionRequest request) {
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (CloudEventReader.IsStructured(request.ContentType)) {
            return true;
        }

        if (CloudEventReader.IsBinary(request.Headers)) {
            var type = TriggerHeaders.Get(request.Headers, CloudEventHeaders.Type);

            return type != null && type.StartsWith(ObjectTypePrefix, StringComparison.Ordinal);
        }

        return request.ContentType != null &&
               request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
    }

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        if (CloudEventReader.IsStructured(request.ContentType) || CloudEventReader.IsBinary(request.Headers)) {
            return FromEvent(request, payload);
        }

        return FromNotification(request, payload);
    }

    /// <summary>The Eventarc form.</summary>
    private static CloudRunTriggerRequest? FromEvent(IExecutionRequest request, TriggerPayload payload) {
        CloudEvent cloudEvent;

        try {
            cloudEvent = CloudEventReader.Read(request.ContentType, request.Headers, payload.Raw);
        }
        catch (CloudEventFormatException) {
            return null;
        }

        if (!cloudEvent.Type.StartsWith(ObjectTypePrefix, StringComparison.Ordinal)) {
            return null;
        }

        var eventType = NotificationEventType(cloudEvent.Type.Substring(ObjectTypePrefix.Length));

        using var document = Metadata(cloudEvent.Data);

        var notification = StorageNotification.From(
            document.RootElement,
            eventType,
            cloudEvent.Time,
            bucket: CloudEventRoutes.LastSegment(cloudEvent.Source),
            name: ObjectName(cloudEvent.Subject));

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, cloudEvent);

        Set(headers, EventTypeHeader, eventType);
        Set(headers, BucketHeader, notification.Bucket);
        Set(headers, ObjectHeader, notification.Name);
        Set(headers, GenerationHeader, notification.Generation);
        Set(headers, EventTimeHeader, cloudEvent.Time);

        return new CloudRunTriggerRequest(
            BlobScheme, "/" + notification.Bucket, notification.Body(), headers, request);
    }

    /// <summary>The Pub/Sub notification form.</summary>
    private static CloudRunTriggerRequest? FromNotification(IExecutionRequest request, TriggerPayload payload) {
        if (payload.Json is not { } root || !PubSubPushBody.TryRead(root, out var message, out var subscription)) {
            return null;
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var data = PubSubPushBody.Read(message, subscription, root, headers);

        var eventType = TriggerHeaders.Get(headers, EventTypeHeader);
        var bucket = TriggerHeaders.Get(headers, BucketHeader);

        if (string.IsNullOrEmpty(eventType) || string.IsNullOrEmpty(bucket)) {
            // A push, but not a Storage notification: it belongs to a queue handler.
            return null;
        }

        using var document = data.Length > 0 ? Metadata(data) : JsonDocument.Parse("{}");

        var notification = StorageNotification.From(
            document.RootElement,
            eventType!,
            TriggerHeaders.Get(headers, EventTimeHeader),
            bucket: bucket,
            name: TriggerHeaders.Get(headers, ObjectHeader)) with {
            Generation = TriggerHeaders.Get(headers, GenerationHeader)
        };

        return new CloudRunTriggerRequest(
            BlobScheme, "/" + notification.Bucket, notification.Body(), headers, request);
    }

    /// <summary>
    /// The notification's word for what the CloudEvent's type says: one vocabulary for both forms.
    /// </summary>
    internal static string NotificationEventType(string suffix) =>
        suffix switch {
            "finalized" => "OBJECT_FINALIZE",
            "deleted" => "OBJECT_DELETE",
            "archived" => "OBJECT_ARCHIVE",
            "metadataUpdated" => "OBJECT_METADATA_UPDATE",
            _ => suffix
        };

    /// <summary>The object's name off the subject, <c>objects/{name}</c>.</summary>
    internal static string? ObjectName(string? subject) {
        const string prefix = "objects/";

        if (string.IsNullOrEmpty(subject)) {
            return null;
        }

        return subject!.StartsWith(prefix, StringComparison.Ordinal) ? subject.Substring(prefix.Length) : subject;
    }

    private static JsonDocument Metadata(ReadOnlyMemory<byte> json) {
        try {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception) {
            throw new InvalidOperationException(
                "The Cloud Storage delivery carries object metadata that is not JSON.", exception);
        }
    }

    private static JsonDocument Metadata(Stream json) {
        try {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception) {
            throw new InvalidOperationException(
                "The Cloud Storage notification carries object metadata that is not JSON.", exception);
        }
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
