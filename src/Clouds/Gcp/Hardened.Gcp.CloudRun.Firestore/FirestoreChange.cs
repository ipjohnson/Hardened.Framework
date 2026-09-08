using Google.Events.Protobuf.Cloud.Firestore.V1;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Firestore;

/// <summary>
/// One change to one document, with the event it came from still attached.
/// </summary>
/// <remarks>
/// <para>
/// The event rides on the request rather than in an injected singleton, for the reason the
/// DynamoDB adapter gives: a singleton holding "the change being handled" is correct only while
/// exactly one is in flight. This is what <see cref="OldValueAttribute"/> reads.
/// </para>
/// <para>
/// A fork keeps the event: <see cref="Clone"/> is overridden so a filter that re-runs the handler
/// still hands it a request <c>[OldValue]</c> can read.
/// </para>
/// </remarks>
public sealed class FirestoreChange : CloudRunTriggerRequest {
    public FirestoreChange(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IExecutionRequest delivery,
        DocumentEventData documentEvent)
        : base(scheme, path, body, headers, delivery) {
        Event = documentEvent;
    }

    private FirestoreChange(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IExecutionRequest delivery,
        ITransportInfo transport,
        DocumentEventData documentEvent)
        : base(scheme, path, body, headers, delivery, transport) {
        Event = documentEvent;
    }

    /// <summary>The document event this request was decoded from.</summary>
    public DocumentEventData Event { get; }

    /// <summary>The document as it is now, or null for a delete.</summary>
    public Document? Value => Event.Value;

    /// <summary>The document as it was, or null for a create.</summary>
    public Document? OldValue => Event.OldValue;

    public override IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) =>
        CopyInto(new FirestoreChange(
            method ?? Method, path ?? Path, Body, headers ?? Headers, Delivery, Transport, Event));
}
