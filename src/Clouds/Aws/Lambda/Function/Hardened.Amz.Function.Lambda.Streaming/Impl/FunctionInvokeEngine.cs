using System.IO.Pipelines;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;

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

    public FunctionInvokeEngine(
        IServiceProvider serviceProvider,
        IFunctionServerProxy serverProxy,
        IMiddlewareService middlewareService,
        IStreamingFunctionRequestMapper requestMapper,
        IMetricLoggerProvider metricLoggerProvider,
        ILambdaContextAccessor lambdaContextAccessor) {
        _serviceProvider = serviceProvider;
        _serverProxy = serverProxy;
        _middlewareService = middlewareService;
        _requestMapper = requestMapper;
        _metricLoggerProvider = metricLoggerProvider;
        _lambdaContextAccessor = lambdaContextAccessor;
    }

    public async Task InvokeAsync(CancellationToken ct) {
        var pipe = new Pipe();

        while (!ct.IsCancellationRequested) {
            Task? responseTask = null;

            try {
                var invocation = await _serverProxy.GetNextInvocation(ct);

                _lambdaContextAccessor.Context = invocation.LambdaContext;

                using var scope = _serviceProvider.CreateScope();

                var responseStream = new ResponseStream(
                    pipe.Writer,
                    () => {
                        responseTask = Task.Run(
                            () => _serverProxy.SendResponse(
                                invocation.RequestId, pipe.Reader, ct), ct);
                    });

                var executionContext = _requestMapper.CreateExecutionContext(
                    _serviceProvider,
                    scope.ServiceProvider,
                    invocation,
                    responseStream,
                    _metricLoggerProvider.CreateLogger("HardenedRequests"));

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
                Console.Error.WriteLine($"[FunctionInvokeEngine] Error processing invocation: {ex}");

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
