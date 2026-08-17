using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using MSLogging = Microsoft.Extensions.Logging;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public interface IApiGatewayEventProcessor {
    Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
}

[SingletonService(Using = RegistrationType.Try)]
public partial class ApiGatewayEventProcessor : IApiGatewayEventProcessor {
    private static readonly MemoryStream _emptyStream = new(Array.Empty<byte>());
    private readonly IServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly IRequestLogger _requestLogger;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly MSLogging.ILogger<ApiGatewayEventProcessor> _logger;
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly IKnownServices _knownServices;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IProxyRequestContextAccessor _requestContextAccessor;

    public ApiGatewayEventProcessor(
        IServiceProvider serviceProvider,
        IMiddlewareService middlewareService,
        IMemoryStreamPool memoryStreamPool,
        IRequestLogger requestLogger,
        MSLogging.ILogger<ApiGatewayEventProcessor> logger,
        IMetricLoggerProvider metricLoggerProvider,
        IKnownServices knownServices,
        ILambdaContextAccessor lambdaContextAccessor,
        IStringBuilderPool stringBuilderPool,
        IProxyRequestContextAccessor requestContextAccessor) {
        _serviceProvider = serviceProvider;
        _middlewareService = middlewareService;
        _memoryStreamPool = memoryStreamPool;
        _requestLogger = requestLogger;
        _logger = logger;
        _metricLoggerProvider = metricLoggerProvider;
        _knownServices = knownServices;
        _lambdaContextAccessor = lambdaContextAccessor;
        _stringBuilderPool = stringBuilderPool;
        _requestContextAccessor = requestContextAccessor;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context) {
        var requestStartTimestamp = MachineTimestamp.Now;

        _lambdaContextAccessor.Context = context;
        _requestContextAccessor.ProxyRequestContext = request.RequestContext;

        var response = new APIGatewayHttpApiV2ProxyResponse();

        using var scope = _serviceProvider.CreateScope();
        using var memoryStreamReservation = _memoryStreamPool.Get();
        using var inputBodyStreamReservation = _memoryStreamPool.Get();

        var executionContext = CreateExecutionContext(scope, request, response, requestStartTimestamp,
            inputBodyStreamReservation.Item);

        executionContext.Response.Body = memoryStreamReservation.Item;

        _requestLogger.RequestBegin(executionContext);

        try {
            var chain = _middlewareService.GetExecutionChain(executionContext);

            await chain.Next();

            // Null means "handled, no opinion" — nothing sets a status on an ordinary success path — and
            // becomes a 200. It no longer means "unmatched": ResourceNotFoundHandler has run by this
            // point and set a 404 if the routing table did not match, which it could not do while the
            // response reported an unset status as 0.
            //
            // Zero is kept as a separate case because it is not a status a handler can have meant and
            // API Gateway renders it as a 502. It is now reachable only by a handler assigning it.
            if (executionContext.Response.Status is null or 0) {
                executionContext.Response.Status = 200;
            }

            CopyHeadersAndCookies(executionContext, response);

            if (executionContext.Response.IsBinary) {
                response.IsBase64Encoded = true;
                response.Body = Convert.ToBase64String(memoryStreamReservation.Item.ToArray());
            }
            else {
                response.Body = Encoding.UTF8.GetString(memoryStreamReservation.Item.ToArray());
            }

            return response;
        }
        catch (Exception exception) {
            // The host-level failure signal, as on Kestrel and both streaming engines.
            // ControllerErrorHelper already reports a handler that threw and stops it escaping, so
            // what reaches here is a filter outside that handling, or the response encoding above -
            // the cases nothing else was reporting.
            //
            // Rethrown deliberately: the Lambda runtime marking the invocation failed is the
            // existing contract, and inventing a 500 here would hide it from retries and the DLQ.
            _requestLogger.RequestFailed(executionContext, exception);

            throw;
        }
        finally {
            // In a finally because these were straight-line statements after the response was
            // encoded, so anything thrown above took the whole close-out with it. Dispose is what
            // writes the EMF line, so a failed invocation reported no duration and no metrics at
            // all - on the main production path, and for exactly the requests worth measuring.
            executionContext.RequestMetrics.Record(RequestMetrics.TotalRequestDuration,
                requestStartTimestamp.GetElapsedMilliseconds());

            _requestLogger.RequestEnd(executionContext);
            executionContext.RequestMetrics.Dispose();
        }
    }

    private void CopyHeadersAndCookies(IExecutionContext executionContext, APIGatewayHttpApiV2ProxyResponse response) {
        var headers = new Dictionary<string, string>();

        if (executionContext.Response.Headers.Count > 0) {
            foreach (var kvp in executionContext.Response.Headers) {
                // ToString() rather than the implicit StringValues conversion, which is nullable
                // (CS8601) and would put a JSON null in the response's header map. Multi-valued
                // headers join on "," either way. Matches IHeaderCollection.ToStringDictionary.
                headers[kvp.Key] = kvp.Value.ToString();
            }
        }

        response.Headers = headers;

        var cookies = executionContext.Response.Cookies.Cookies;

        if (cookies.Count > 0) {
            using var stringBuilderReservation = _stringBuilderPool.Get();
            var stringBuilder = stringBuilderReservation.Item;
            var cookieArray = new string[cookies.Count];
            var i = 0;
            foreach (var cookiePair in cookies) {
                stringBuilder.Append(cookiePair.Key);
                stringBuilder.Append('=');
                // Item1 — the value. Appending the Tuple itself, which is what this did until
                // 2026-08-11, resolves to StringBuilder.Append(object) and emits the tuple's
                // ToString(), so every Set-Cookie read
                // "name=(value, CookieSetOptions { Expires = , ... })".
                stringBuilder.Append(cookiePair.Value.Item1);
                cookiePair.Value.Item2.AppendSettings(stringBuilder);
                cookieArray[i] = stringBuilder.ToString();
                i++;
                stringBuilder.Clear();
            }

            response.Cookies = cookieArray;
        }
        else {
            response.Cookies = Array.Empty<string>();
        }
    }

    private IExecutionContext CreateExecutionContext(IServiceScope scope,
        APIGatewayHttpApiV2ProxyRequest request,
        APIGatewayHttpApiV2ProxyResponse response,
        MachineTimestamp starTime, MemoryStream memoryStream) {
        return new ApiGatewayV2ExecutionContext(
            _serviceProvider,
            scope.ServiceProvider,
            _knownServices,
            new ApiGatewayV2ExecutionRequest(request) { Body = CreateBodyFromRequest(request, memoryStream) },
            new ApiGatewayV2ExecutionResponse(response),
            _metricLoggerProvider.CreateLogger("HardenedRequests"),
            starTime);
    }

    private Stream CreateBodyFromRequest(APIGatewayHttpApiV2ProxyRequest request, MemoryStream memoryStream) {
        if (string.IsNullOrEmpty(request.Body)) {
            return _emptyStream;
        }

        byte[] bytes = request.IsBase64Encoded
            ? Convert.FromBase64String(request.Body)
            : Encoding.UTF8.GetBytes(request.Body);

        memoryStream.Write(bytes, 0, bytes.Length);
        memoryStream.Position = 0;

        return memoryStream;
    }
}