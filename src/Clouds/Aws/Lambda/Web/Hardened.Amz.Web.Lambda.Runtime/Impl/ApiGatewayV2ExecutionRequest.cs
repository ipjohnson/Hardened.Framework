using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

internal class ApiGatewayV2ExecutionRequest : IExecutionRequest {
    private readonly APIGatewayHttpApiV2ProxyRequest _proxyRequest;
    private readonly string _method;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;
    private IReadOnlyList<string>? _cookies;

    public ApiGatewayV2ExecutionRequest(APIGatewayHttpApiV2ProxyRequest request)
        : this(request, null, null, null, null, null) {
    }

    private ApiGatewayV2ExecutionRequest(
        APIGatewayHttpApiV2ProxyRequest request,
        string? method,
        string? path,
        IHeaderCollection? headers,
        IQueryStringCollection? queryString,
        IReadOnlyList<string>? cookies) {
        _proxyRequest = request;
        _method = method ?? request.RequestContext.Http.Method;
        Path = path ?? StripStagePath(request.RawPath, request.RequestContext?.Stage);
        _headerCollection = headers;
        _queryStringCollection = queryString;
        _cookies = cookies;

        // Stream.Null rather than a shared static MemoryStream. Nothing writes to it, but a single
        // instance handed to every request in a warm container is a position that one caller can
        // move under another.
        Body = Stream.Null;
    }

    private static string StripStagePath(string rawPath, string? stage) {
        if (!string.IsNullOrEmpty(stage) && rawPath.StartsWith("/" + stage)) {
            return rawPath.Substring(stage.Length + 1);
        }

        return rawPath;
    }

    /// <summary>
    /// Every argument here is applied. All five used to be accepted and discarded, so a fork could
    /// not rebind anything: <c>Clone(method: "DELETE")</c> returned a clone still reporting the
    /// original's method, because <c>Method</c> and <c>Path</c> read through to the shared proxy
    /// request. Any filter forking a chain to re-run a handler against a different method, path or
    /// header set silently re-ran it against the original. Fixed 2026-08-15; the framework asserts
    /// this across transports in
    /// <c>Hardened.Requests.Testing.Conformance.ExecutionRequestConformanceTests</c>.
    /// </summary>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new ApiGatewayV2ExecutionRequest(
            _proxyRequest,
            method ?? _method,
            path ?? Path,
            CloneHeaders(headers),
            queryString ?? _queryStringCollection,
            cookies ?? _cookies) {
            // Cloned, not shared: a forked chain must be able to rebind without writing
            // through to the request it was forked from. See the conformance suite in
            // Hardened.Requests.Testing.
            Parameters = Parameters?.Clone(),
            Body = Body,
            PathTokens = PathTokens,
        };
    }

    /// <summary>
    /// A supplied set replaces; otherwise the clone gets a copy of whatever this request currently
    /// has, so that setting a header in a fork does not write through to the request it forked
    /// from. Null carries through as null, leaving the clone to build the same collection from the
    /// proxy request the first time it is asked.
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

    public string Method => _method;

    public string Path { get; }

    public string? ContentType {
        get {
            if (Headers.TryGet("Content-Type", out var value)) {
                return value;
            }

            return "application/json";
        }
    }

    public string? Accept {
        get {
            if (Headers.TryGet("Accept", out var value)) {
                return value;
            }

            return "application/json";
        }
    }

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues(_proxyRequest.Headers);

    IDictionary<string, StringValues> IExecutionRequest.Headers => Headers;

    
    public IQueryStringCollection QueryString => _queryStringCollection ??=
        new SimpleQueryStringCollection(_proxyRequest.QueryStringParameters);

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>
    /// Empty rather than null when the request carried no cookies. API Gateway omits the field
    /// entirely in that case, so <c>APIGatewayHttpApiV2ProxyRequest.Cookies</c> is null, and
    /// handing that back through a non-nullable <see cref="IReadOnlyList{T}"/> made every caller a
    /// null-reference away from failing on the ordinary case of a request without cookies.
    /// </summary>
    public IReadOnlyList<string> Cookies =>
        _cookies ??= _proxyRequest.Cookies ?? Array.Empty<string>();
}