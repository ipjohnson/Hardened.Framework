using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

public class AspNetExecutionContext : IExecutionContext {
    private HttpContext _httpContext;

    public AspNetExecutionContext(HttpContext httpContext, IMetricLogger logger) {
        _httpContext = httpContext;
        KnownServices = httpContext.RequestServices.GetRequiredService<IKnownServices>();
        Request = new AspNetExecutionRequest(httpContext.Request);
        Response = new AspNetExecutionResponse(httpContext.Response);
        StartTime = MachineTimestamp.Now;
        RequestMetrics = logger;
    }

    private AspNetExecutionContext(
        HttpContext httpContext,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        IMetricLogger metricLogger,
        MachineTimestamp startTime) {
        _httpContext = httpContext;
        KnownServices = knownServices;
        Request = request;
        Response = response;
        RequestMetrics = metricLogger;
        StartTime = startTime;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request,
        IExecutionResponse? response,
        IServiceProvider? serviceProvider,
        IMetricLogger? metricLogger) {
        return new AspNetExecutionContext(
            _httpContext,
            KnownServices,
            request ?? Request,
            response ?? Response,
            metricLogger ?? RequestMetrics,
            StartTime) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
        };
    }

    public IServiceProvider RootServiceProvider => _httpContext.RequestServices;

    public IKnownServices KnownServices { get; }
    public IServiceProvider RequestServices => _httpContext.RequestServices;
    public IExecutionRequest Request { get; }
    public IExecutionResponse Response { get; }
    public object? HandlerInstance { get; set; }
    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }
    public DefaultOutputFunc? DefaultOutput { get; set; }
    public IMetricLogger RequestMetrics { get; }
    public MachineTimestamp StartTime { get; }
    public CancellationToken CancellationToken => _httpContext.RequestAborted;
}

public class AspNetExecutionRequest : IExecutionRequest {
    private readonly HttpRequest _httpRequest;

    // Values supplied by Clone. Null means "fall through to the underlying HttpRequest",
    // which is what keeps Clone(x: null) preserving the current value.
    private readonly string? _methodOverride;
    private readonly string? _pathOverride;
    private readonly IDictionary<string, StringValues>? _headersOverride;
    private readonly IQueryStringCollection? _queryStringOverride;
    private readonly IReadOnlyList<string>? _cookiesOverride;

    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryString;
    private IReadOnlyList<string>? _cookies;

    public AspNetExecutionRequest(HttpRequest httpRequest) {
        _httpRequest = httpRequest;
    }

    private AspNetExecutionRequest(
        HttpRequest httpRequest,
        string? methodOverride,
        string? pathOverride,
        IDictionary<string, StringValues>? headersOverride,
        IQueryStringCollection? queryStringOverride,
        IReadOnlyList<string>? cookiesOverride) {
        _httpRequest = httpRequest;
        _methodOverride = methodOverride;
        _pathOverride = pathOverride;
        _headersOverride = headersOverride;
        _queryStringOverride = queryStringOverride;
        _cookiesOverride = cookiesOverride;
    }

    /// <summary>
    /// A null argument keeps the current value, a non-null argument replaces it.
    /// </summary>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new AspNetExecutionRequest(
            _httpRequest,
            method ?? _methodOverride,
            path ?? _pathOverride,
            headers ?? _headersOverride,
            queryString ?? _queryStringOverride,
            cookies ?? _cookiesOverride) {
            // Cloned, not shared: a forked chain must be able to rebind without writing
            // through to the request it was forked from. See the conformance suite.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens,
        };
    }

    public string Method => _methodOverride ?? _httpRequest.Method;

    public string Path => _pathOverride ?? _httpRequest.Path;

    public string? ContentType => Headers.GetOrDefault(KnownHeaders.ContentType);

    public string? Accept => Headers.GetOrDefault(KnownHeaders.Accept);

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body {
        get => _httpRequest.Body;
        set => _httpRequest.Body = value;
    }

    public IDictionary<string, StringValues> Headers => _headersOverride ?? _httpRequest.Headers;

    public IQueryStringCollection QueryString =>
        _queryStringOverride ?? (_queryString ??= new SimpleQueryStringCollection(
            _httpRequest.Query.ToDictionary(q => q.Key, q => q.Value.ToString())));

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>
    /// ASP.NET Core parses the Cookie header into name/value pairs; the framework contract
    /// is the raw <c>name=value</c> form that API Gateway v2 delivers, so reassemble it.
    /// </summary>
    public IReadOnlyList<string> Cookies =>
        _cookiesOverride ?? (_cookies ??=
            _httpRequest.Cookies.Select(cookie => $"{cookie.Key}={cookie.Value}").ToList());
}

public class AspNetExecutionResponse : IExecutionResponse {
    private HttpResponse _httpResponse;
    private int? _status;

    public AspNetExecutionResponse(HttpResponse httpResponse) {
        _httpResponse = httpResponse;
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new AspNetExecutionResponse(_httpResponse) {
            _status = _status,
            ResponseValue = ResponseValue,
            ShouldCompress = ShouldCompress,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
        };
    }

    public string? ContentType {
        get => _httpResponse.ContentType;
        set => _httpResponse.ContentType = value ?? "";
    }

    public object? ResponseValue { get; set; }

    /// <summary>
    /// Null while the status is still undecided; otherwise what will be, or has been, sent.
    ///
    /// This used to read straight back off <see cref="HttpResponse.StatusCode"/>, which looks
    /// equivalent and is not: ASP.NET initialises that to 200, so the getter never returned null.
    /// <c>ResourceNotFoundHandler</c> only supplies a 404 when it finds the status still unset, so
    /// it never fired on this host — unmatched routes came back as 404 only because
    /// <c>AspNetCoreRequestHandler</c> falls through to the terminal delegate, which sets one.
    /// The Lambda runtime, which normalises a null status to 200 before responding, documents the
    /// intended contract; <c>TestExecutionResponse</c> implements it the same way.
    ///
    /// The <c>HasStarted</c> fallback covers the other end of the request. Nothing sets a status
    /// on an ordinary success path, so a plain nullable field would still read null at
    /// <c>RequestEnd</c> and log a blank status for every successful request. Once the response
    /// has started the status is settled, so reporting it is accurate rather than a guess — and
    /// every filter that tests for null runs earlier than that, before anything is written.
    /// </summary>
    public int? Status {
        get => _status ?? (_httpResponse.HasStarted ? _httpResponse.StatusCode : null);
        set {
            _status = value;
            _httpResponse.StatusCode = value ?? 200;
        }
    }

    public bool ShouldCompress { get; set; }

    public Stream Body {
        get => _httpResponse.Body;
        set => _httpResponse.Body = value;
    }

    public IDictionary<string, StringValues> Headers => _httpResponse.Headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => _httpResponse.HasStarted;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; } = new CookieSetCollectionImpl();

    public bool ShouldSerialize { get; set; } = true;
}
