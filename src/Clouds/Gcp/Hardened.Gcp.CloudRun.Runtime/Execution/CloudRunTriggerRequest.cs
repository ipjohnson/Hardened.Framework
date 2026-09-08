using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Execution;

/// <summary>
/// The trigger-shaped request an envelope unwraps to: a record, a route, and the delivery it
/// arrived in.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped, per D7, the way a queue message is on Lambda: the handler sees the message the
/// publisher sent and the route the source gives it, not the POST that carried them. A push's
/// <c>message.data</c> is the body, its attributes are the headers, and its metadata is a header
/// each under a prefixed name. The empties are answers rather than gaps - a push has no query
/// string and no cookie - which is why this enrols in the payload conformance profile.
/// </para>
/// <para>
/// <b>Built new rather than cloned from what Kestrel made</b>, because <c>Clone</c> cannot replace
/// a body. The delivery is kept as <see cref="Delivery"/>: its headers are where a push's
/// authorization token is, and its transport is what this request's own transport answers with,
/// plus the three Cloud Run facts.
/// </para>
/// <para>
/// Open for an adapter whose record carries more than a body: a Firestore change keeps the
/// document event it was decoded from, so <c>[OldValue]</c> can read the previous document off
/// the request. Such a subclass overrides <see cref="Clone"/> so a fork keeps what it carries.
/// </para>
/// </remarks>
public class CloudRunTriggerRequest : IExecutionRequest {
    private IPathTokenCollection? _pathTokens;

    /// <param name="scheme">The trigger scheme - <c>QUEUE</c>, <c>TOPIC</c>, <c>EVENT</c>, <c>TIMER</c>.</param>
    /// <param name="path">The source's own name as a rooted path.</param>
    /// <param name="body">The message, which the request owns from here on.</param>
    /// <param name="headers">The message's attributes and metadata. Copied to a case-insensitive collection when not one already.</param>
    /// <param name="delivery">The web-shaped request the envelope arrived in.</param>
    public CloudRunTriggerRequest(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IExecutionRequest delivery)
        : this(scheme, path, body, headers, delivery, new CloudRunTransportInfo(delivery.Transport)) {
    }

    /// <summary>For a clone, and for a subclass: the transport is shared rather than built again.</summary>
    protected CloudRunTriggerRequest(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IExecutionRequest delivery,
        ITransportInfo transport) {
        Method = scheme;
        Path = path;
        Body = body;
        Headers = HeaderCollectionStringValues.EnsureCaseInsensitive(headers);
        Delivery = delivery;
        Transport = transport;
    }

    /// <summary>The scheme, which is a plain string rather than an HTTP verb.</summary>
    public string Method { get; }

    /// <summary>The source's own name - a subscription, a topic, a bucket - as a path.</summary>
    public string Path { get; }

    /// <summary>The HTTP request the envelope arrived in.</summary>
    public IExecutionRequest Delivery { get; }

    public string? ContentType => Headers.GetOrDefault(KnownHeaders.ContentType);

    public string? Accept => Headers.GetOrDefault(KnownHeaders.Accept);

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IDictionary<string, StringValues> Headers { get; }

    /// <summary>Nothing. A message carries no query string.</summary>
    public IQueryStringCollection QueryString => EmptyQueryStringCollection.Instance;

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>Nothing. There is no header to parse cookies from and no client to have set one.</summary>
    public IReadOnlyList<string> Cookies => Array.Empty<string>();

    /// <summary>
    /// The delivery's connection, plus the service, revision and configuration Cloud Run set.
    /// </summary>
    public ITransportInfo Transport { get; }

    /// <remarks>
    /// Null keeps the current value, a non-null argument replaces it, matching the rest of the
    /// <c>Clone</c> contract. The query string and cookies arguments are accepted and ignored,
    /// because this shape has nowhere to put them. The transport is shared rather than cloned: a
    /// fork is the same message on the same connection.
    /// </remarks>
    public virtual IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) =>
        CopyInto(new CloudRunTriggerRequest(
            method ?? Method, path ?? Path, Body, headers ?? Headers, Delivery, Transport));

    /// <summary>
    /// What every clone carries besides its constructor arguments: its own parameters, and the
    /// path tokens routing set.
    /// </summary>
    /// <remarks>
    /// <c>Parameters?.Clone()</c>, not the same instance: a forked chain rebinds its own
    /// parameters, and sharing them would let one fork overwrite another's. Null stays null.
    /// </remarks>
    protected TRequest CopyInto<TRequest>(TRequest clone) where TRequest : CloudRunTriggerRequest {
        clone.Parameters = Parameters?.Clone();
        clone.PathTokens = PathTokens;

        return clone;
    }
}
