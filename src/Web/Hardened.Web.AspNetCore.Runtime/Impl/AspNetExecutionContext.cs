using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.QueryString;
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
    private HttpRequest _httpRequest;

    public AspNetExecutionRequest(HttpRequest httpRequest) {
        _httpRequest = httpRequest;
    }

    public IExecutionRequest Clone(
        string? method,
        string? path,
        IDictionary<string, StringValues> headers,
        IQueryStringCollection queryString,
        IReadOnlyList<string> cookies) {
        return new AspNetExecutionRequest(_httpRequest) {
            Parameters = Parameters,
            Body = Body,
            PathTokens = PathTokens,
        };
    }

    public string Method => _httpRequest.Method;

    public string Path => _httpRequest.Path;

    public string? ContentType => _httpRequest.ContentType;

    public string? Accept => null;

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body {
        get => _httpRequest.Body;
        set => _httpRequest.Body = value;
    }

    public IDictionary<string, StringValues> Headers => _httpRequest.Headers;

    public IQueryStringCollection QueryString =>
        new SimpleQueryStringCollection(
            _httpRequest.Query.ToDictionary(q => q.Key, q => q.Value.ToString()));

    public IPathTokenCollection PathTokens { get; set; }

    public IReadOnlyList<string> Cookies => new List<string>();
}

public class AspNetExecutionResponse : IExecutionResponse {
    private HttpResponse _httpResponse;

    public AspNetExecutionResponse(HttpResponse httpResponse) {
        _httpResponse = httpResponse;
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new AspNetExecutionResponse(_httpResponse) {
            ResponseValue = ResponseValue,
            TemplateName = TemplateName,
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

    public string? TemplateName { get; set; }

    public int? Status {
        get => _httpResponse.StatusCode;
        set => _httpResponse.StatusCode = value ?? 200;
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
