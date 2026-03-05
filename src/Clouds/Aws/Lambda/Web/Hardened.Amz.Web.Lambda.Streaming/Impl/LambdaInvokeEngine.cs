using System.IO.Pipelines;
using System.Text.Json;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Web.Lambda.Streaming.Serializer;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public interface ILambdaInvokeEngine {
    Task InvokeAsync(CancellationToken ct);
}

public class LambdaInvokeEngine : ILambdaInvokeEngine {
    private readonly IServiceProvider _serviceProvider;
    private readonly ILambdaServerProxy _serverProxy;
    private readonly IMiddlewareService _middlewareService;
    private readonly IStreamingRequestMapper _requestMapper;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly ILogger<LambdaInvokeEngine> _logger;

    public LambdaInvokeEngine(
        IServiceProvider serviceProvider,
        ILambdaServerProxy serverProxy,
        IMiddlewareService middlewareService,
        IStreamingRequestMapper requestMapper,
        IMetricLoggerProvider metricLoggerProvider,
        ILambdaContextAccessor lambdaContextAccessor,
        ILogger<LambdaInvokeEngine> logger) {
        _serviceProvider = serviceProvider;
        _serverProxy = serverProxy;
        _middlewareService = middlewareService;
        _requestMapper = requestMapper;
        _metricLoggerProvider = metricLoggerProvider;
        _lambdaContextAccessor = lambdaContextAccessor;
        _logger = logger;
    }

    public async Task InvokeAsync(CancellationToken ct) {
        var pipe = new Pipe();

        while (!ct.IsCancellationRequested) {
            Task? responseTask = null;

            try {
                var invocation = await _serverProxy.GetNextInvocation(ct);

                _lambdaContextAccessor.Context = invocation.LambdaContext;

                using var scope = _serviceProvider.CreateScope();
                using var bodyStream = new MemoryStream();

                var responseStream = new ResponseStream(
                    pipe.Writer,
                    WritePrelude,
                    () => {
                        responseTask = Task.Run(
                            () => _serverProxy.SendResponse(
                                invocation.RequestId, pipe.Reader, ct), ct);
                    });

                var executionContext = _requestMapper.CreateExecutionContext(
                    _serviceProvider,
                    scope.ServiceProvider,
                    invocation.Request,
                    responseStream,
                    bodyStream,
                    _metricLoggerProvider.CreateLogger("HardenedRequests"));

                responseStream.SetExecutionResponse(executionContext.Response);

                var chain = _middlewareService.GetExecutionChain(executionContext);
                await chain.Next();

                await responseStream.FlushAsync(ct);
                await pipe.Writer.CompleteAsync();

                if (responseTask != null) {
                    await responseTask;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error processing Lambda invocation");

                if (responseTask == null) {
                    // Response not started yet — try to complete pipe and report error
                    try {
                        await pipe.Writer.CompleteAsync();
                    }
                    catch {
                        // Ignore pipe completion errors
                    }
                }
                else {
                    // Response already started — complete pipe so POST finishes
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

    private static void WritePrelude(IExecutionResponse response, PipeWriter writer) {
        var headers = new Dictionary<string, string>();

        if (response.Headers.Count > 0) {
            foreach (var kvp in response.Headers) {
                headers[kvp.Key] = kvp.Value.ToString();
            }
        }

        if (response.ContentType != null) {
            headers["Content-Type"] = response.ContentType;
        }

        var cookies = new List<string>();
        var responseCookies = response.Cookies.Cookies;

        if (responseCookies.Count > 0) {
            foreach (var cookie in responseCookies) {
                cookies.Add($"{cookie.Key}={cookie.Value.Item1}");
            }
        }

        var prelude = new ResponsePrelude {
            StatusCode = response.Status ?? 200,
            Headers = headers,
            Cookies = cookies
        };

        var stream = writer.AsStream(leaveOpen: true);
        JsonSerializer.Serialize(stream, prelude, StreamingEventSerializerContext.Default.ResponsePrelude);
        stream.Write("\0\0\0\0\0\0\0\0"u8);
    }
}
