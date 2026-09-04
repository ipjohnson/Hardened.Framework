using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Core.ResponseStreaming;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// Serves one API Gateway event in stream mode: the response body is a <see cref="ResponseStream"/>
/// whose first byte opens the Lambda response stream with the status and headers as they stand.
/// </summary>
public interface IStreamingEventProcessor {
    /// <param name="request">The payload format 2.0 event.</param>
    /// <param name="context">The invocation's context.</param>
    /// <param name="streams">
    /// Where the stream is opened. The host passes the runtime's; the local harness passes one that
    /// writes to the ASP.NET response; a test passes one that captures the prelude and the bytes.
    /// </param>
    Task Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context, IResponseStreamFactory streams);
}

/// <summary>
/// One mechanism, three timings. A streaming operation opens the stream at its first item and
/// writes one chunk per item; a buffered operation opens it when the serializer writes the body,
/// one write and a close; a refusal opens it with the refusal's status when the error serializer
/// writes. The processor never asks which it is running.
/// </summary>
/// <remarks>
/// <para>
/// Errors before the first byte are the pipeline's: it serializes them, and that first byte opens
/// the stream with the error's status, so the client gets a complete, correctly typed response.
/// An exception that escapes the chain after the first byte is not caught here. It reaches the
/// bootstrap, which writes it as trailers and records the invocation as failed - which is what a
/// truncated stream should be, and what the hand-rolled host never reported.
/// </para>
/// <para>
/// A response that ends with nothing written still opens the stream and writes a newline. A
/// streamed response with an empty body leaves CloudFront waiting for data that never comes.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class StreamingEventProcessor : IStreamingEventProcessor {
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private readonly IServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly IRequestLogger _requestLogger;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly IKnownServices _knownServices;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IProxyRequestContextAccessor _requestContextAccessor;

    public StreamingEventProcessor(
        IServiceProvider serviceProvider,
        IMiddlewareService middlewareService,
        IMemoryStreamPool memoryStreamPool,
        IRequestLogger requestLogger,
        IMetricLoggerProvider metricLoggerProvider,
        IKnownServices knownServices,
        ILambdaContextAccessor lambdaContextAccessor,
        IStringBuilderPool stringBuilderPool,
        IProxyRequestContextAccessor requestContextAccessor) {
        _serviceProvider = serviceProvider;
        _middlewareService = middlewareService;
        _memoryStreamPool = memoryStreamPool;
        _requestLogger = requestLogger;
        _metricLoggerProvider = metricLoggerProvider;
        _knownServices = knownServices;
        _lambdaContextAccessor = lambdaContextAccessor;
        _stringBuilderPool = stringBuilderPool;
        _requestContextAccessor = requestContextAccessor;
    }

    public async Task Process(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context, IResponseStreamFactory streams) {
        var requestStartTimestamp = MachineTimestamp.Now;

        _lambdaContextAccessor.Context = context;
        _requestContextAccessor.ProxyRequestContext = request.RequestContext;

        using var scope = _serviceProvider.CreateScope();
        using var inputBodyStreamReservation = _memoryStreamPool.Get();

        StreamingExecutionResponse? response = null;

        // The prelude is built when the stream opens, not when the response is created, so it
        // carries whatever the pipeline decided by the first byte.
        var body = new ResponseStream(() => streams.CreateHttpStream(Prelude(response!)));

        response = new StreamingExecutionResponse(body);

        var executionContext = new ApiGatewayV2ExecutionContext(
            _serviceProvider,
            scope.ServiceProvider,
            _knownServices,
            new ApiGatewayV2ExecutionRequest(request) {
                Body = ApiGatewayEventMapping.RequestBody(request, inputBodyStreamReservation.Item)
            },
            response,
            _metricLoggerProvider.CreateLogger("HardenedRequests"),
            requestStartTimestamp);

        _requestLogger.RequestBegin(executionContext);

        try {
            var chain = _middlewareService.GetExecutionChain(executionContext);

            await chain.Next();

            if (body.Length == 0) {
                await body.WriteAsync(NewLine);
            }

            await body.CompleteAsync();
        }
        catch (Exception exception) {
            _requestLogger.RequestFailed(executionContext, exception);

            // What was written before the failure still goes, so the client's view of the stream
            // is the handler's up to the point it broke. The pump's own failure, if any, is second
            // to the one being reported.
            try {
                await body.CompleteAsync();
            }
            catch {
                // The exception in flight is the one to surface.
            }

            throw;
        }
        finally {
            executionContext.RequestMetrics.Record(RequestMetrics.TotalRequestDuration,
                requestStartTimestamp.GetElapsedMilliseconds());

            _requestLogger.RequestEnd(executionContext);
            executionContext.RequestMetrics.Dispose();
        }
    }

    /// <summary>
    /// The status, headers and cookies as they stand when the first byte is written.
    /// </summary>
    /// <remarks>
    /// Null and zero become 200 for the reason <see cref="ApiGatewayEventProcessor"/> gives: null
    /// is "handled, no opinion", and zero is not a status a handler can have meant.
    /// </remarks>
    private HttpResponseStreamPrelude Prelude(IExecutionResponse response) {
        var prelude = new HttpResponseStreamPrelude {
            StatusCode = (HttpStatusCode)(response.Status is null or 0 ? 200 : response.Status.Value),
            Headers = ApiGatewayEventMapping.Headers(response.Headers),
            Cookies = ApiGatewayEventMapping.Cookies(response.Cookies, _stringBuilderPool)
        };

        return prelude;
    }
}
