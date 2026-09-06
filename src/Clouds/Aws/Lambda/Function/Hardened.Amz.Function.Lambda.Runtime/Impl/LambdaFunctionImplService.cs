using Amazon.Lambda.Core;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

/// <summary>
/// Service that handles lambda invoke request,
/// takes a stream in and returns a stream
/// </summary>
public interface ILambdaFunctionImplService {
    Task<Stream> InvokeFunction(Stream stream, ILambdaContext context);
}

/// <summary>
/// The one invocation path for a function, whichever runtime drives it: the managed runtime
/// through the generated <c>Invoke</c>, or <c>LambdaBootstrap</c> through the generated
/// <c>Main</c>.
/// </summary>
/// <remarks>
/// In buffered mode the body collects and comes back as the returned stream. In stream mode the
/// body is a <see cref="ResponseStream"/> that opens the Lambda response stream, with no prelude,
/// at its first byte; the returned stream is empty and the bootstrap ignores it. A function whose
/// operation never writes leaves no stream open, so the bootstrap sends the empty return as an
/// ordinary response rather than timing the invocation out - which is what the hand-rolled
/// streaming engine did when nothing started the response.
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class LambdaFunctionImplService : ILambdaFunctionImplService {
    private const string MetricNamespace = "HardenedRequests";

    private readonly IServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly IKnownServices _knownServices;
    private readonly ILambdaContextAccessor _contextAccessor;
    private readonly IRequestLogger _requestLogger;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IResponseStreamFactory _streams;
    private readonly LambdaResponseMode _mode;

    public LambdaFunctionImplService(
        IMiddlewareService middlewareService,
        IMemoryStreamPool memoryStreamPool,
        IServiceProvider serviceProvider,
        IKnownServices knownServices,
        ILambdaContextAccessor contextAccessor,
        IRequestLogger requestLogger,
        IMetricLoggerProvider metricLoggerProvider,
        IResponseStreamFactory streams,
        IOptions<ILambdaResponseModeConfiguration> mode) {
        _middlewareService = middlewareService;
        _memoryStreamPool = memoryStreamPool;
        _serviceProvider = serviceProvider;
        _knownServices = knownServices;
        _contextAccessor = contextAccessor;
        _requestLogger = requestLogger;
        _metricLoggerProvider = metricLoggerProvider;
        _streams = streams;
        _mode = mode.Value.Mode;
    }

    public async Task<Stream> InvokeFunction(Stream stream, ILambdaContext context) {
        _contextAccessor.Context = context;

        var now = MachineTimestamp.Now;

        await using var requestContext = _serviceProvider.CreateAsyncScope();

        var customContext = context.ClientContext?.Custom;

        var headers = new Dictionary<string, StringValues>();

        if (customContext != null) {
            foreach (var kvp in customContext) {
                headers[kvp.Key] = kvp.Value;
            }
        }

        var request =
            new LambdaExecutionRequest("Invoke", context.FunctionName, stream, headers);

        ResponseStream? streamed = null;
        MemoryStreamPoolWrapper? buffered = null;
        Stream body;

        if (_mode == LambdaResponseMode.Stream) {
            body = streamed = new ResponseStream(_streams.CreateStream);
        }
        else {
            body = buffered = new MemoryStreamPoolWrapper(_memoryStreamPool.Get());
        }

        var response = new LambdaExecutionResponse(body, new HeaderCollectionStringValues());

        var metricLogger = _metricLoggerProvider.CreateLogger(MetricNamespace);

        var lambdaExecutionContext = new LambdaExecutionContext(
            _serviceProvider,
            requestContext.ServiceProvider,
            _knownServices,
            request,
            response,
            metricLogger,
            now);

        _requestLogger.RequestBegin(lambdaExecutionContext);

        try {
            await _middlewareService.GetExecutionChain(lambdaExecutionContext).Next();

            if (streamed != null) {
                await streamed.CompleteAsync();

                return Stream.Null;
            }

            buffered!.Position = 0;

            return buffered;
        }
        catch (Exception exception) {
            _requestLogger.RequestFailed(lambdaExecutionContext, exception);

            buffered?.Dispose();

            if (streamed != null) {
                // What was written before the failure still goes; the exception in flight is the
                // one the bootstrap reports, as trailers if the stream opened.
                try {
                    await streamed.CompleteAsync();
                }
                catch {
                    // The exception in flight is the one to surface.
                }
            }

            throw;
        }
        finally {
            metricLogger.Record(RequestMetrics.TotalRequestDuration, now.GetElapsedMilliseconds());

            _requestLogger.RequestEnd(lambdaExecutionContext);
            metricLogger.Dispose();
        }
    }
}
