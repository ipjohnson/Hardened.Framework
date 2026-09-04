using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Abstract.Outputs;
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

    /// <summary>
    /// What <see cref="CancellationToken"/> returns once something has assigned one, and null
    /// until then.
    /// </summary>
    /// <remarks>
    /// A plain backing field captured at construction would quietly change behaviour here: ASP.NET
    /// middleware may replace <c>IHttpRequestLifetimeFeature</c> mid-request, which is why this is
    /// read through on every get rather than captured once. Falling through until something assigns
    /// keeps that exactly as it was, up to the moment a filter states a deadline.
    /// </remarks>
    private CancellationToken? _cancellationToken;

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
        MachineTimestamp startTime,
        CancellationToken? cancellationToken) {
        _httpContext = httpContext;
        KnownServices = knownServices;
        Request = request;
        Response = response;
        RequestMetrics = metricLogger;
        StartTime = startTime;
        // The nullable rather than the value, so a clone taken outside a deadline still reads
        // through to the feature instead of pinning whatever it returned at that moment.
        _cancellationToken = cancellationToken;
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
            StartTime,
            _cancellationToken) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same request, so it reports one id rather than two.
            CorrelationId = CorrelationId,
        };
    }

    public IServiceProvider RootServiceProvider => _httpContext.RequestServices;

    public IKnownServices KnownServices { get; }
    public IServiceProvider RequestServices => _httpContext.RequestServices;
    public IExecutionRequest Request { get; }
    public IExecutionResponse Response { get; }

    /// <summary>
    /// Hardened's own principal, not <c>HttpContext.User</c>. Bridging the two is an opt-in adapter
    /// rather than the default, so moving a handler between this host and another does not change
    /// how it authenticates.
    /// </summary>
    public ICallerPrincipal CallerPrincipal { get; set; } = AnonymousCallerPrincipal.Instance;

    private string? _correlationId;

    /// <inheritdoc />
    /// <remarks>
    /// Reads the trace id rather than <c>HttpContext.TraceIdentifier</c>. ASP.NET's hosting layer
    /// has already started an activity by the time this is built, so the trace id is both available
    /// and the one the rest of the trace is filed under - where TraceIdentifier is a
    /// connection-scoped string in a different shape that nothing else here would recognise.
    /// </remarks>
    public string CorrelationId {
        get => _correlationId ??= CorrelationIdentifier.ForCurrentTrace();
        init => _correlationId = value;
    }

    public object? HandlerInstance { get; set; }
    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }
    public DefaultOutputFunc? DefaultOutput { get; set; }
    public IMetricLogger RequestMetrics { get; }
    public MachineTimestamp StartTime { get; }
    public CancellationToken CancellationToken {
        get => _cancellationToken ?? _httpContext.RequestAborted;
        set => _cancellationToken = value;
    }
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
    private ITransportInfo? _transport;

    public AspNetExecutionRequest(HttpRequest httpRequest) {
        _httpRequest = httpRequest;
    }

    private AspNetExecutionRequest(
        HttpRequest httpRequest,
        string? methodOverride,
        string? pathOverride,
        IDictionary<string, StringValues>? headersOverride,
        IQueryStringCollection? queryStringOverride,
        IReadOnlyList<string>? cookiesOverride,
        ITransportInfo? transport) {
        _httpRequest = httpRequest;
        _transport = transport;
        _methodOverride = methodOverride;
        _pathOverride = pathOverride;

        // A fork is handed a plain dictionary, which is very likely case-sensitive, while the
        // request it forked from was reading ASP.NET's own case-insensitive header collection. Left
        // alone the override silently changes how header names resolve for the rest of that chain.
        _headersOverride = headersOverride is null
            ? null
            : HeaderCollectionStringValues.EnsureCaseInsensitive(headersOverride);
        _queryStringOverride = queryStringOverride;
        _cookiesOverride = cookiesOverride;
    }

    /// <summary>
    /// Built once and shared with every fork, because a fork is the same request on the same
    /// connection.
    /// </summary>
    public ITransportInfo Transport => _transport ??= new AspNetTransportInfo(_httpRequest);

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
            cookies ?? _cookiesOverride,
            // The same instance, not a fresh one over the same request: a fork is the same request
            // on the same connection, and the conformance suite asserts identity rather than
            // equality because that is the property callers rely on.
            Transport) {
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

    /// <summary>
    /// ASP.NET Core has already parsed and decoded the query, repeats included, so this only
    /// changes the shape. It used to flatten each value with <c>ToString()</c>, which joined a
    /// repeated key into one comma-separated string and left the collection unable to say there
    /// had been more than one.
    /// </summary>
    public IQueryStringCollection QueryString =>
        _queryStringOverride ?? (_queryString ??= new SimpleQueryStringCollection(
            _httpRequest.Query.ToDictionary(q => q.Key, q => q.Value)));

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
            OutputFactory = OutputFactory,
            Output = Output,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
        };
    }

    public string? ContentType {
        get => _httpResponse.ContentType;
        set => _httpResponse.ContentType = value ?? "";
    }

    public object? ResponseValue { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

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

    public Stream Body {
        get => _httpResponse.Body;
        set => _httpResponse.Body = value;
    }

    public IDictionary<string, StringValues> Headers => _httpResponse.Headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => _httpResponse.HasStarted;

    public bool IsBinary { get; set; }

    /// <summary>
    /// Header backed, and lazily. Over HTTP a cookie reaches the client as <c>Set-Cookie</c>, and
    /// nothing on this host serialised the recording collection that used to sit here — so
    /// <c>Response.Cookies.Append(...)</c> compiled, ran, and the client never saw it. The same
    /// defect the Kestrel host had, and the one Hardened.Amz fixed on 2026-08-11.
    /// </summary>
    public ICookieSetCollection Cookies =>
        _cookies ??= new HeaderCookieSetCollection(_httpResponse.Headers);

    private ICookieSetCollection? _cookies;

    public bool ShouldSerialize { get; set; } = true;
}
