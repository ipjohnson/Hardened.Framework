using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public interface IStreamingRequestMapper {
    IExecutionContext CreateExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        APIGatewayHttpApiV2ProxyRequest proxyRequest,
        ResponseStream responseStream,
        MemoryStream bodyStream,
        IMetricLogger metricLogger);
}

[SingletonService(Using = RegistrationType.Try)]
public class StreamingRequestMapper : IStreamingRequestMapper {
    private readonly IKnownServices _knownServices;

    public StreamingRequestMapper(IKnownServices knownServices) {
        _knownServices = knownServices;
    }

    public IExecutionContext CreateExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        APIGatewayHttpApiV2ProxyRequest proxyRequest,
        ResponseStream responseStream,
        MemoryStream bodyStream,
        IMetricLogger metricLogger) {
        var request = CreateRequest(proxyRequest, bodyStream);
        var response = new StreamingExecutionResponse(responseStream);

        return new StreamingExecutionContext(
            rootServiceProvider,
            requestServices,
            _knownServices,
            request,
            response,
            metricLogger,
            MachineTimestamp.Now);
    }

    private static StreamingExecutionRequest CreateRequest(
        APIGatewayHttpApiV2ProxyRequest proxyRequest,
        MemoryStream bodyStream) {
        Stream body;

        if (!string.IsNullOrEmpty(proxyRequest.Body)) {
            var bytes = proxyRequest.IsBase64Encoded
                ? Convert.FromBase64String(proxyRequest.Body)
                : Encoding.UTF8.GetBytes(proxyRequest.Body);

            bodyStream.Write(bytes, 0, bytes.Length);
            bodyStream.Position = 0;
            body = bodyStream;
        }
        else {
            body = Stream.Null;
        }

        return new StreamingExecutionRequest(proxyRequest) { Body = body };
    }
}

public class StreamingExecutionRequest : IExecutionRequest {
    private readonly APIGatewayHttpApiV2ProxyRequest _proxyRequest;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;

    public StreamingExecutionRequest(APIGatewayHttpApiV2ProxyRequest request) {
        _proxyRequest = request;
        Path = StripStagePath(request.RawPath, request.RequestContext?.Stage);
    }

    private static string StripStagePath(string rawPath, string? stage) {
        if (!string.IsNullOrEmpty(stage) && rawPath.StartsWith("/" + stage)) {
            return rawPath.Substring(stage.Length + 1);
        }

        return rawPath;
    }

    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new StreamingExecutionRequest(_proxyRequest) {
            // Cloned, not shared: a forked chain must be able to rebind without writing
            // through to the request it was forked from. See the conformance suite in
            // Hardened.Requests.Testing.
            Parameters = Parameters?.Clone(),
            Body = Body,
            PathTokens = PathTokens,
        };
    }

    public string Method => _proxyRequest.RequestContext.Http.Method;
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
    public Stream Body { get; set; } = Stream.Null;

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues(_proxyRequest.Headers);

    IDictionary<string, StringValues> IExecutionRequest.Headers => Headers;

    public IQueryStringCollection QueryString => _queryStringCollection ??=
        new SimpleQueryStringCollection(_proxyRequest.QueryStringParameters);

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    public IReadOnlyList<string> Cookies => _proxyRequest.Cookies ?? Array.Empty<string>();
}

public class StreamingExecutionResponse : IExecutionResponse {
    private HeaderCollectionStringValues? _headerCollection;
    private readonly StreamingCookieSetCollection _cookies = new();

    public StreamingExecutionResponse(Stream responseStream) {
        Body = responseStream;
    }

    public object Clone() {
        return Clone(null);
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new StreamingExecutionResponse(Body!) {
            ResponseValue = ResponseValue,
            TemplateName = TemplateName,
            ShouldCompress = ShouldCompress,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
        };
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }
    public string? TemplateName { get; set; }
    public int? Status { get; set; }
    public bool ShouldCompress { get; set; }
    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues();

    IDictionary<string, StringValues> IExecutionResponse.Headers => Headers;

    public Exception? ExceptionValue { get; set; }
    public bool ResponseStarted => Body?.Position > 0;
    public bool IsBinary { get; set; }
    public ICookieSetCollection Cookies => _cookies;
    public bool ShouldSerialize { get; set; } = true;
}

public class StreamingCookieSetCollection : ICookieSetCollection {
    private readonly Dictionary<string, Tuple<string, CookieSetOptions>> _cookies = new();

    public void Append(string cookieName, string cookieValue, CookieSetOptions? options = null) {
        _cookies[cookieName] = new Tuple<string, CookieSetOptions>(
            cookieValue, options ?? new CookieSetOptions());
    }

    public IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies => _cookies;
}

public class StreamingExecutionContext : IExecutionContext {
    public StreamingExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        IMetricLogger requestMetrics,
        MachineTimestamp startTime) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        KnownServices = knownServices;
        Request = request;
        Response = response;
        RequestMetrics = requestMetrics;
        StartTime = startTime;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request,
        IExecutionResponse? response,
        IServiceProvider? serviceProvider,
        IMetricLogger? metricLogger) {
        return new StreamingExecutionContext(
            RootServiceProvider,
            serviceProvider ?? RequestServices,
            KnownServices,
            request ?? Request,
            response ?? Response,
            metricLogger ?? RequestMetrics,
            StartTime);
    }

    public IServiceProvider RootServiceProvider { get; }
    public IKnownServices KnownServices { get; }
    public IServiceProvider RequestServices { get; }
    public IExecutionRequest Request { get; }
    public IExecutionResponse Response { get; }
    public object? HandlerInstance { get; set; }
    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }
    public DefaultOutputFunc? DefaultOutput { get; set; }
    public IMetricLogger RequestMetrics { get; }
    public MachineTimestamp StartTime { get; }
    public CancellationToken CancellationToken { get; } = CancellationToken.None;
}
