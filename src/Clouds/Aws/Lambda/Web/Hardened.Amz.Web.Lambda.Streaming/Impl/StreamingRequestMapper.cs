using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
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
    private readonly string _method;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;
    private IReadOnlyList<string>? _cookies;
    private ITransportInfo? _transport;

    public StreamingExecutionRequest(APIGatewayHttpApiV2ProxyRequest request)
        : this(request, null, null, null, null, null, null) {
    }

    private StreamingExecutionRequest(
        APIGatewayHttpApiV2ProxyRequest request,
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
    }

    private static string StripStagePath(string rawPath, string? stage) {
        if (!string.IsNullOrEmpty(stage) && rawPath.StartsWith("/" + stage)) {
            return rawPath.Substring(stage.Length + 1);
        }

        return rawPath;
    }

    /// <summary>
    /// Every argument here is applied. All five used to be accepted and discarded — the same defect
    /// the buffered transport carried, in the same shape. See
    /// <c>ApiGatewayV2ExecutionRequest.Clone</c>.
    /// </summary>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new StreamingExecutionRequest(
            _proxyRequest,
            method ?? _method,
            path ?? Path,
            CloneHeaders(headers),
            queryString ?? _queryStringCollection,
            cookies ?? _cookies,
            // Shared rather than rebuilt, as on the buffered path: a fork is the same request from
            // the same caller.
            Transport) {
            // Cloned, not shared: a forked chain must be able to rebind without writing
            // through to the request it was forked from. See the conformance suite in
            // Hardened.Requests.Testing.
            Parameters = Parameters?.Clone(),
            Body = Body,
            PathTokens = PathTokens,
        };
    }

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

    /// <summary>
    /// The buffered path's, reused rather than copied.
    /// </summary>
    /// <remarks>
    /// It is the same API Gateway event either way, so a second implementation would be two
    /// answers to one question - which is the drift AMZ-FEATURE-REVIEW item 17 is about. This
    /// package already references the buffered runtime, so there is nothing to pay for it.
    /// </remarks>
    public ITransportInfo Transport =>
        _transport ??= new Web.Lambda.Runtime.Impl.ApiGatewayTransportInfo(_proxyRequest);

    public IReadOnlyList<string> Cookies =>
        _cookies ??= _proxyRequest.Cookies ?? Array.Empty<string>();
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
            OutputFactory = OutputFactory,
            Output = Output,
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

    /// <summary>
    /// Built from <see cref="OutputFactory"/> on first use and kept. Replaces <c>TemplateName</c>,
    /// which named a view by string and is gone - a view is a type now.
    /// </summary>
    public IHardenedResponseOutput? Output { get; set; }

    /// <summary>Builds what writes this response, or null when it is serialized like any other.</summary>
    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

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
            StartTime) {
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same request, so it reports one id rather than two.
            CorrelationId = CorrelationId
        };
    }

    public IServiceProvider RootServiceProvider { get; }
    public IKnownServices KnownServices { get; }
    public IServiceProvider RequestServices { get; }
    public IExecutionRequest Request { get; }
    public IExecutionResponse Response { get; }

    /// <inheritdoc />
    public ICallerPrincipal CallerPrincipal { get; set; } = AnonymousCallerPrincipal.Instance;

    private string? _correlationId;

    /// <inheritdoc />
    /// <remarks>
    /// Realized on first read rather than at construction, so it is the trace id when anything is
    /// collecting traces - the host starts the span after building the context.
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
    public CancellationToken CancellationToken { get; } = CancellationToken.None;
}
