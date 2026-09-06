using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Execution;

/// <summary>
/// The payload-shaped request: a record, a route, and no connection.
/// </summary>
/// <remarks>
/// <para>
/// One of the two shapes every adapter produces, per D7. A queue message, a stream record, a timer
/// and a direct invoke all arrive as this; API Gateway, a function URL and a CloudWatch widget
/// arrive as the web-shaped one. The shape is about what the handler sees rather than how the bytes
/// travelled, which is why a widget is web-shaped despite being delivered as a direct invoke.
/// </para>
/// <para>
/// <b>The empties below are answers, not gaps.</b> A queue message has no query string and no
/// cookies - not an empty one, no such concept - and returning empty is the honest answer to a
/// question the transport cannot be asked. That is also why this enrols in
/// <c>PayloadExecutionRequestConformanceTests</c> rather than the web profile: the three assertions
/// the web profile adds need the transport to have <em>carried</em> a value, and no adapter for this
/// shape can make them true however it is written.
/// </para>
/// </remarks>
public class LambdaPayloadRequest : IExecutionRequest {
    private IPathTokenCollection? _pathTokens;

    public LambdaPayloadRequest(
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
    /// <c>STREAM</c>, <c>TIMER</c>, <c>INVOKE</c>.
    /// </summary>
    /// <remarks>
    /// The slot on <see cref="IExecutionRequest"/> was always a string, which is what makes a
    /// non-HTTP source cost nothing: <c>QUEUE /orders-new</c> routes through the same table as
    /// <c>GET /orders/{id}</c>.
    /// </remarks>
    public string Method { get; }

    /// <summary>
    /// The source's own name - a queue, a topic, a table, an operation - as a path.
    /// </summary>
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
    /// Nothing, because an invocation through the Lambda API has no connection to describe.
    /// </summary>
    /// <remarks>
    /// A function invoked by the SDK, by an event source or by a console has no client address, no
    /// protocol version and no scheme. Inventing one from the invoking identity would put something
    /// in a field callers read as the caller's network address.
    /// </remarks>
    public ITransportInfo Transport => EmptyTransportInfo.Instance;

    /// <remarks>
    /// Null means keep the current value, matching the rest of the <c>Clone</c> contract. Passing
    /// headers straight through would leave <see cref="Headers"/> null on a bare <c>Clone()</c>,
    /// which the interface permits.
    ///
    /// <para>
    /// <paramref name="queryString"/> and <paramref name="cookies"/> are accepted and ignored,
    /// because this shape has nowhere to put them. The conformance suite does not yet assert that
    /// <c>Clone</c> replaces either; tightening that is a separate change from establishing the
    /// contract, and on this shape the answer would be to refuse rather than to store.
    /// </para>
    /// </remarks>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new LambdaPayloadRequest(
            method ?? Method, path ?? Path, Body, headers ?? Headers) {
            // Clone(), not the same instance: a forked chain rebinds its own parameters, and
            // sharing them would let one record in a batch overwrite another's. Null stays null,
            // because a request that has not been bound yet has nothing to copy. The old
            // implementation set neither, which is one of the reasons it enrolled in no suite.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens
        };
    }
}
