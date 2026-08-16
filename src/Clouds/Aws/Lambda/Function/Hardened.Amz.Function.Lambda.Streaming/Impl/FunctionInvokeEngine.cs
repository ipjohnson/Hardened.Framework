using System.IO.Pipelines;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

public interface IFunctionInvokeEngine {
    Task InvokeAsync(CancellationToken ct);
}

[SingletonService(Using = RegistrationType.Try)]
public class FunctionInvokeEngine : IFunctionInvokeEngine {
    private readonly IServiceProvider _serviceProvider;
    private readonly IFunctionServerProxy _serverProxy;
    private readonly IMiddlewareService _middlewareService;
    private readonly IStreamingFunctionRequestMapper _requestMapper;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IRequestLogger _requestLogger;
    private readonly ILogger<FunctionInvokeEngine> _logger;

    public FunctionInvokeEngine(
        IServiceProvider serviceProvider,
        IFunctionServerProxy serverProxy,
        IMiddlewareService middlewareService,
        IStreamingFunctionRequestMapper requestMapper,
        IMetricLoggerProvider metricLoggerProvider,
        ILambdaContextAccessor lambdaContextAccessor,
        IRequestLogger requestLogger,
        ILogger<FunctionInvokeEngine> logger) {
        _serviceProvider = serviceProvider;
        _serverProxy = serverProxy;
        _middlewareService = middlewareService;
        _requestMapper = requestMapper;
        _metricLoggerProvider = metricLoggerProvider;
        _lambdaContextAccessor = lambdaContextAccessor;
        _requestLogger = requestLogger;
        _logger = logger;
    }

    public async Task InvokeAsync(CancellationToken ct) {
        var pipe = new Pipe();

        while (!ct.IsCancellationRequested) {
            Task? responseTask = null;

            // Hoisted so the catch can report the failure against the request it belongs to, and
            // the finally can close the request out on both paths.
            IExecutionContext? executionContext = null;
            IMetricLogger? metricLogger = null;
            MachineTimestamp? requestStartTimestamp = null;

            try {
                var invocation = await _serverProxy.GetNextInvocation(ct);

                // After GetNextInvocation, which blocks until there is work. Taking it earlier
                // would bill the idle wait to the request.
                requestStartTimestamp = MachineTimestamp.Now;

                _lambdaContextAccessor.Context = invocation.LambdaContext;

                using var scope = _serviceProvider.CreateScope();

                var responseStream = new ResponseStream(
                    pipe.Writer,
                    () => {
                        responseTask = Task.Run(
                            () => _serverProxy.SendResponse(
                                invocation.RequestId, pipe.Reader, ct), ct);
                    });

                metricLogger = _metricLoggerProvider.CreateLogger("HardenedRequests");

                executionContext = _requestMapper.CreateExecutionContext(
                    _serviceProvider,
                    scope.ServiceProvider,
                    invocation,
                    responseStream,
                    metricLogger);

                _requestLogger.RequestBegin(executionContext);

                var chain = _middlewareService.GetExecutionChain(executionContext);
                await chain.Next();

                await responseStream.FlushAsync(ct);

                await pipe.Writer.FlushAsync(ct);

                await pipe.Writer.CompleteAsync();

                if (responseTask != null) {
                    await responseTask;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                if (executionContext != null) {
                    _requestLogger.RequestFailed(executionContext, ex);
                }
                else {
                    // No context yet - the invocation itself could not be read, so there is no
                    // request to attribute this to.
                    _logger.LogError(ex, "Error processing invocation");
                }

                if (responseTask == null) {
                    try {
                        await pipe.Writer.CompleteAsync();
                    }
                    catch {
                        // Ignore pipe completion errors
                    }
                }
                else {
                    try {
                        await pipe.Writer.CompleteAsync(ex);
                        await responseTask;
                    }
                    catch {
                        // Ignore errors during cleanup
                    }
                }
            }
            finally {
                // Both paths. A request that failed is the one whose duration is worth having, and
                // Dispose is what writes the EMF line - without it this engine reported nothing at
                // all, because the logger went straight into the context with no local to close.
                if (metricLogger != null) {
                    if (requestStartTimestamp.HasValue) {
                        metricLogger.Record(
                            RequestMetrics.TotalRequestDuration,
                            requestStartTimestamp.Value.GetElapsedMilliseconds());
                    }

                    metricLogger.Dispose();
                }

                if (executionContext != null) {
                    _requestLogger.RequestEnd(executionContext);
                }

                try {
                    await pipe.Writer.CompleteAsync();
                }
                catch {
                    // Ignore — may already be completed
                }

                try {
                    await pipe.Reader.CompleteAsync();
                }
                catch {
                    // Ignore — may already be completed
                }

                pipe.Reset();
            }
        }
    }
}
