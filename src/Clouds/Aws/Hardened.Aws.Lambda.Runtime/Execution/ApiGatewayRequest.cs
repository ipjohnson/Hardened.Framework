using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Execution;

/// <summary>
/// The web-shaped request, from an API Gateway payload format 2.0 event.
/// </summary>
/// <remarks>
/// Also a function URL and an ALB, which deliver the same shape. Payload format 1.0 is a different
/// shape and gets its own adapter when one is written; it is not this class with a branch in it,
/// which is what the design means by a payload format being a value rather than a fork.
/// </remarks>
public class ApiGatewayRequest : IExecutionRequest {
    private readonly APIGatewayHttpApiV2ProxyRequest _proxyRequest;
    private readonly string _method;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;
    private IReadOnlyList<string>? _cookies;
    private ITransportInfo? _transport;

    public ApiGatewayRequest(APIGatewayHttpApiV2ProxyRequest request, Stream body)
        : this(request, body, null, null, null, null, null, null) {
    }

    private ApiGatewayRequest(
        APIGatewayHttpApiV2ProxyRequest request,
        Stream body,
        string? method,
        string? path,
        IHeaderCollection? headers,
        IQueryStringCollection? queryString,
        IReadOnlyList<string>? cookies,
        ITransportInfo? transport) {
        _proxyRequest = request;
        _transport = transport;
        _method = method ?? request.RequestContext.Http.Method;
        Path = path ?? StripStagePath(request.RawPath, request.RequestContext?.Stage);
        _headerCollection = headers;
        _queryStringCollection = queryString;
        _cookies = cookies;
        Body = body;
    }

    /// <summary>
    /// The stage prefix a REST-style deployment puts on <c>rawPath</c>, removed so a route matches
    /// the same template whatever stage it is deployed to.
    /// </summary>
    private static string StripStagePath(string rawPath, string? stage) {
        if (!string.IsNullOrEmpty(stage) && rawPath.StartsWith("/" + stage)) {
            return rawPath.Substring(stage!.Length + 1);
        }

        return rawPath;
    }

    public string Method => _method;

    public string Path { get; }

    /// <remarks>
    /// Defaults to JSON when the gateway carried no header, which is the format the overwhelming
    /// majority of callers send and what the binder would have to assume anyway.
    /// </remarks>
    public string? ContentType =>
        Headers.TryGet(KnownHeaders.ContentType, out var value) ? value : "application/json";

    public string? Accept =>
        Headers.TryGet(KnownHeaders.Accept, out var value) ? value : "application/json";

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues(_proxyRequest.Headers);

    IDictionary<string, StringValues> IExecutionRequest.Headers => Headers;

    /// <remarks>
    /// API Gateway hands over <c>queryStringParameters</c> already percent-decoded, so nothing here
    /// decodes a second time. That is the difference the conformance suite's decoding assertion
    /// exists to hold: a timestamp's <c>+</c>, a base64 <c>=</c> and a space have to arrive as
    /// themselves whatever the transport did to carry them, and a transport that parses its own
    /// query string is where that goes wrong.
    /// </remarks>
    public IQueryStringCollection QueryString => _queryStringCollection ??=
        new SimpleQueryStringCollection(_proxyRequest.QueryStringParameters);

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>
    /// Empty rather than null when the request carried no cookies.
    /// </summary>
    /// <remarks>
    /// API Gateway omits the field entirely in that case, so
    /// <c>APIGatewayHttpApiV2ProxyRequest.Cookies</c> is null, and handing that back through a
    /// non-nullable <see cref="IReadOnlyList{T}"/> made every caller a null reference away from
    /// failing on the ordinary case of a request without cookies.
    /// </remarks>
    public IReadOnlyList<string> Cookies =>
        _cookies ??= _proxyRequest.Cookies ?? Array.Empty<string>();

    /// <summary>
    /// Built once and shared with every fork, because a fork is the same request from the same
    /// caller. The conformance suite asserts the identity, not just the values, and caught the
    /// framework's own ASP.NET adapter getting this wrong.
    /// </summary>
    public ITransportInfo Transport =>
        _transport ??= new ApiGatewayTransportInfo(_proxyRequest);

    /// <summary>
    /// Every argument is applied.
    /// </summary>
    /// <remarks>
    /// All five used to be accepted and discarded, so a fork could not rebind anything:
    /// <c>Clone(method: "DELETE")</c> returned a clone still reporting the original's method,
    /// because <c>Method</c> and <c>Path</c> read through to the shared proxy request. Any filter
    /// forking a chain to re-run a handler against a different method, path or header set silently
    /// re-ran it against the original.
    /// </remarks>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new ApiGatewayRequest(
            _proxyRequest,
            Body,
            method ?? _method,
            path ?? Path,
            CloneHeaders(headers),
            queryString ?? _queryStringCollection,
            cookies ?? _cookies,
            // Shared rather than rebuilt: a fork is the same request from the same caller, and
            // rebinding its method or path says nothing about where it came from.
            Transport) {
            // Cloned, not shared: a forked chain must be able to rebind without writing through to
            // the request it was forked from.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens
        };
    }

    /// <summary>
    /// A supplied set replaces; otherwise the clone gets a copy of whatever this request currently
    /// has, so setting a header in a fork does not write through to the request it forked from.
    /// Null carries through as null, leaving the clone to build the same collection from the proxy
    /// request the first time it is asked.
    /// </summary>
    private IHeaderCollection? CloneHeaders(IDictionary<string, StringValues>? headers) {
        if (headers != null) {
            return new HeaderCollectionStringValues(headers);
        }

        return _headerCollection == null
            ? null
            : new HeaderCollectionStringValues(
                new Dictionary<string, StringValues>(_headerCollection));
    }
}
