using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Runtime.Execution;

/// <summary>
/// The payload-shaped request: a record, a route, and no connection.
/// </summary>
/// <remarks>
/// <para>
/// One of the two shapes every adapter produces. A Service Bus message, an Event Hubs event, a
/// change feed document and a timer all arrive as this; the HTTP trigger arrives as the web-shaped
/// one. The shape is about what the handler sees rather than how the bytes travelled.
/// </para>
/// <para>
/// <b>The empties below are answers, not gaps.</b> A queue message has no query string and no
/// cookies - not an empty one, no such concept - and returning empty is the honest answer to a
/// question the transport cannot be asked. That is also why this enrols in
/// <c>PayloadExecutionRequestConformanceTests</c> rather than the web profile.
/// </para>
/// <para>
/// A copy of <c>LambdaPayloadRequest</c> rather than a reference to it, by design: the two lines
/// share conventions and not types, so neither can break the other's contract by changing its own.
/// </para>
/// </remarks>
public class FunctionsPayloadRequest : IExecutionRequest {
    private IPathTokenCollection? _pathTokens;

    public FunctionsPayloadRequest(
        string method,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers) {
        Method = method;
        Path = path;
        Body = body;
        Headers = headers;
    }

    /// <summary>
    /// The scheme, which is a plain string rather than an HTTP verb: <c>QUEUE</c>, <c>TOPIC</c>,
    /// <c>STREAM</c>, <c>TIMER</c>.
    /// </summary>
    public string Method { get; }

    /// <summary>The source's own name - a queue, a topic, a container - as a path.</summary>
    public string Path { get; }

    public string? ContentType => Headers.GetOrDefault("Content-Type");

    public string? Accept => Headers.GetOrDefault("Accept");

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IDictionary<string, StringValues> Headers { get; }

    /// <summary>Nothing. A queue message carries no query string.</summary>
    public IQueryStringCollection QueryString => EmptyQueryStringCollection.Instance;

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>Nothing. There is no header to parse cookies from and no client to have set one.</summary>
    public IReadOnlyList<string> Cookies => Array.Empty<string>();

    /// <summary>
    /// Nothing, because an invocation the host hands a worker has no connection to describe.
    /// </summary>
    public ITransportInfo Transport => EmptyTransportInfo.Instance;

    /// <remarks>
    /// Null means keep the current value, matching the rest of the <c>Clone</c> contract.
    /// <paramref name="queryString"/> and <paramref name="cookies"/> are accepted and ignored,
    /// because this shape has nowhere to put them.
    /// </remarks>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new FunctionsPayloadRequest(
            method ?? Method, path ?? Path, Body, headers ?? Headers) {
            // Clone(), not the same instance: a forked chain rebinds its own parameters, and
            // sharing them would let one message in a batch overwrite another's.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens
        };
    }
}
