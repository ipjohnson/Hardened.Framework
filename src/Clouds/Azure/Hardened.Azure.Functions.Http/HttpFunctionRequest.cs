using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Http;

/// <summary>
/// The web-shaped request, from the worker's <see cref="HttpRequestData"/>.
/// </summary>
/// <remarks>
/// The counterpart of <c>LambdaHttpRequest</c>, and simpler because the worker has already done
/// the transport's work: the query is parsed and decoded, the headers are a collection, the
/// cookies are objects and the body is a stream. What is left is naming each the way the pipeline
/// asks for it.
/// </remarks>
public class HttpFunctionRequest : IExecutionRequest {
    private readonly string _method;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;
    private IReadOnlyList<string>? _cookies;
    private ITransportInfo? _transport;

    public HttpFunctionRequest(HttpRequestData request, string? routedPath)
        : this(request, request.Body, null, routedPath ?? request.Url.AbsolutePath, null, null, null, null) {
    }

    private HttpFunctionRequest(
        HttpRequestData request,
        Stream body,
        string? method,
        string path,
        IHeaderCollection? headers,
        IQueryStringCollection? queryString,
        IReadOnlyList<string>? cookies,
        ITransportInfo? transport) {
        Data = request;
        Body = body;
        _method = method ?? request.Method;
        Path = path;
        _headerCollection = headers;
        _queryStringCollection = queryString;
        _cookies = cookies;
        _transport = transport;
    }

    /// <summary>The worker's request, which the adapter answers through.</summary>
    public HttpRequestData Data { get; }

    public string Method => _method;

    public string Path { get; }

    /// <remarks>
    /// Defaults to JSON when the request carried no header, which is the format the overwhelming
    /// majority of callers send and what the binder would have to assume anyway.
    /// </remarks>
    public string? ContentType =>
        Headers.TryGet(KnownHeaders.ContentType, out var value) ? value : "application/json";

    public string? Accept =>
        Headers.TryGet(KnownHeaders.Accept, out var value) ? value : "application/json";

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers => _headerCollection ??= ReadHeaders(Data);

    IDictionary<string, StringValues> IExecutionRequest.Headers => Headers;

    /// <remarks>
    /// The worker's <see cref="HttpRequestData.Query"/> is already percent-decoded, so nothing
    /// here decodes a second time; a timestamp's <c>+</c> and a base64 <c>=</c> arrive as
    /// themselves.
    /// </remarks>
    public IQueryStringCollection QueryString => _queryStringCollection ??= ReadQuery(Data);

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>The cookies as <c>name=value</c> strings, empty rather than null when there are none.</summary>
    public IReadOnlyList<string> Cookies =>
        _cookies ??= Data.Cookies.Select(cookie => cookie.Name + "=" + cookie.Value).ToArray();

    /// <summary>Built once and shared with every fork, because a fork is the same request from the same caller.</summary>
    public ITransportInfo Transport => _transport ??= new HttpFunctionTransportInfo(Data);

    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new HttpFunctionRequest(
            Data,
            Body,
            method ?? _method,
            path ?? Path,
            CloneHeaders(headers),
            queryString ?? _queryStringCollection,
            cookies ?? _cookies,
            Transport) {
            // Cloned, not shared: a forked chain must be able to rebind without writing through to
            // the request it was forked from.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens
        };
    }

    private IHeaderCollection? CloneHeaders(IDictionary<string, StringValues>? headers) {
        if (headers != null) {
            return new HeaderCollectionStringValues(headers);
        }

        return _headerCollection == null
            ? null
            : new HeaderCollectionStringValues(new Dictionary<string, StringValues>(_headerCollection));
    }

    private static IHeaderCollection ReadHeaders(HttpRequestData request) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers) {
            headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        return new HeaderCollectionStringValues(headers);
    }

    private static IQueryStringCollection ReadQuery(HttpRequestData request) {
        var query = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in request.Query.AllKeys) {
            if (key == null) {
                continue;
            }

            query[key] = new StringValues(request.Query.GetValues(key));
        }

        return new SimpleQueryStringCollection(query);
    }
}
