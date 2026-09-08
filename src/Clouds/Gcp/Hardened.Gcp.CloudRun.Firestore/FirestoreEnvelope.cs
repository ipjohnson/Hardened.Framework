using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf;
using Hardened.CloudEvents;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Firestore;

/// <summary>
/// A Firestore document event delivered through Eventarc: a CloudEvent of type
/// <c>google.cloud.firestore.document.v1.created</c>, <c>updated</c>, <c>deleted</c> or
/// <c>written</c>, whose data is a <c>DocumentEventData</c> in protobuf.
/// </summary>
/// <remarks>
/// <para>
/// Routes as <c>CHANGE /orders</c>, the collection taken from <c>ce-subject</c>
/// (<c>documents/orders/o-1</c>, or <c>documents/users/u-1/orders/o-1</c> for a subcollection,
/// where it is the collection the document is in). The body is the document as it is now, its
/// fields written as plain JSON by <see cref="FirestoreValueJson"/>, so a handler binds
/// <c>Order</c> and never sees a typed value; a delete binds the old value, because there is no
/// new one and a handler told only that something was deleted cannot say what.
/// <see cref="OldValueAttribute"/> reaches the previous document for the other events.
/// </para>
/// <para>
/// The headers are the <c>ce-</c> attributes, the document's path as
/// <see cref="DocumentHeader"/>, and its full resource name as <see cref="DocumentNameHeader"/>.
/// The event type is <c>ce-type</c>, which is how a handler that cares tells the four apart.
/// </para>
/// </remarks>
public sealed class FirestoreEnvelope : ITriggerEnvelope {
    /// <summary>The scheme a change feed routes under, which <c>[Change]</c> declares.</summary>
    public const string ChangeScheme = "CHANGE";

    /// <summary>What every document CloudEvent's type starts with.</summary>
    public const string DocumentTypePrefix = "google.cloud.firestore.document.v1.";

    /// <summary>The document's path within the database, <c>orders/o-1</c>.</summary>
    public const string DocumentHeader = "x-goog-firestore-document";

    /// <summary>The document's full resource name, <c>projects/p/databases/d/documents/orders/o-1</c>.</summary>
    public const string DocumentNameHeader = "x-goog-firestore-document-name";

    private const string Documents = "documents/";

    public bool Recognises(IExecutionRequest request) {
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (CloudEventReader.IsStructured(request.ContentType)) {
            return true;
        }

        var type = TriggerHeaders.Get(request.Headers, CloudEventHeaders.Type);

        return type != null && type.StartsWith(DocumentTypePrefix, StringComparison.Ordinal);
    }

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        CloudEvent cloudEvent;

        try {
            cloudEvent = CloudEventReader.Read(request.ContentType, request.Headers, payload.Raw);
        }
        catch (CloudEventFormatException) {
            return null;
        }

        if (!cloudEvent.Type.StartsWith(DocumentTypePrefix, StringComparison.Ordinal)) {
            return null;
        }

        DocumentEventData documentEvent;

        try {
            documentEvent = DocumentEventData.Parser.ParseFrom(cloudEvent.Data.Span);
        }
        catch (InvalidProtocolBufferException exception) {
            throw new InvalidOperationException(
                "The Firestore delivery carries data that is not a DocumentEventData.", exception);
        }

        var document = documentEvent.Value ?? documentEvent.OldValue;
        var path = DocumentPath(cloudEvent.Subject);

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, cloudEvent);

        Set(headers, DocumentHeader, path);
        Set(headers, DocumentNameHeader, document?.Name);

        return new FirestoreChange(
            ChangeScheme,
            "/" + Collection(path),
            FirestoreValueJson.Body(document),
            headers,
            request,
            documentEvent);
    }

    /// <summary>The document's path without the <c>documents/</c> prefix the subject carries.</summary>
    internal static string DocumentPath(string? subject) {
        if (string.IsNullOrEmpty(subject)) {
            return "";
        }

        return subject!.StartsWith(Documents, StringComparison.Ordinal)
            ? subject.Substring(Documents.Length)
            : subject;
    }

    /// <summary>
    /// The collection a document path names: the segment before the document's own id.
    /// </summary>
    /// <remarks>
    /// Paths alternate collection and document, so <c>orders/o-1</c> is in <c>orders</c> and
    /// <c>users/u-1/orders/o-1</c> is in <c>orders</c> too. A path with one segment is a
    /// collection on its own, and an empty one gives an empty route, which is a route no handler
    /// declared rather than a failure inside the envelope.
    /// </remarks>
    internal static string Collection(string documentPath) {
        var segments = documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length switch {
            0 => "",
            1 => segments[0],
            _ => segments[segments.Length - 2]
        };
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
