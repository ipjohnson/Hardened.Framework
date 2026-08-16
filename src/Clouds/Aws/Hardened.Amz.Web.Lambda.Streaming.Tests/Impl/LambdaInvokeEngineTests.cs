using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class LambdaInvokeEngineTests {
    private readonly IServiceProvider _serviceProvider;
    private readonly ILambdaServerProxy _serverProxy;
    private readonly IMiddlewareService _middlewareService;
    private readonly IStreamingRequestMapper _requestMapper;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IMetricLogger _metricLogger;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IRequestLogger _requestLogger;
    private readonly LambdaInvokeEngine _engine;

    public LambdaInvokeEngineTests() {
        _serviceProvider = CreateServiceProvider();
        _serverProxy = Substitute.For<ILambdaServerProxy>();
        _middlewareService = Substitute.For<IMiddlewareService>();
        _requestMapper = Substitute.For<IStreamingRequestMapper>();
        _metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
        _metricLogger = Substitute.For<IMetricLogger>();
        _lambdaContextAccessor = Substitute.For<ILambdaContextAccessor>();
        _requestLogger = Substitute.For<IRequestLogger>();

        _metricLoggerProvider.CreateLogger(Arg.Any<string>())
            .Returns(_metricLogger);

        _engine = new LambdaInvokeEngine(
            _serviceProvider,
            _serverProxy,
            _middlewareService,
            _requestMapper,
            _metricLoggerProvider,
            _lambdaContextAccessor,
            _requestLogger);
    }

    /// <summary>
    /// One invocation, then a cancellation so the loop exits. Returns the context the mapper will
    /// hand the engine, which is what the assertions are written against.
    /// </summary>
    private IExecutionContext ArrangeSingleInvocation(Task chainResult) {
        var cts = new CancellationTokenSource();
        var invocation = CreateInvocationData();
        var callCount = 0;

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns(_ => {
                callCount++;

                if (callCount > 1) {
                    cts.Cancel();

                    throw new OperationCanceledException(cts.Token);
                }

                return Task.FromResult(invocation);
            });

        var executionContext = Substitute.For<IExecutionContext>();
        executionContext.Response.Returns(Substitute.For<IExecutionResponse>());

        _requestMapper.CreateExecutionContext(
            Arg.Any<IServiceProvider>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<APIGatewayHttpApiV2ProxyRequest>(),
            Arg.Any<ResponseStream>(),
            Arg.Any<MemoryStream>(),
            Arg.Any<IMetricLogger>()
        ).Returns(executionContext);

        var chain = Substitute.For<IExecutionChain>();
        chain.Next().Returns(chainResult);
        _middlewareService.GetExecutionChain(executionContext).Returns(chain);

        _cancellation = cts;

        return executionContext;
    }

    private CancellationTokenSource _cancellation = new();

    private static IServiceProvider CreateServiceProvider() {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        return provider;
    }

    private InvocationData CreateInvocationData() {
        return new InvocationData {
            Request = new APIGatewayHttpApiV2ProxyRequest {
                RawPath = "/test",
                Headers = new Dictionary<string, string>(),
                RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                    Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                        Method = "GET",
                        Path = "/test"
                    }
                }
            },
            LambdaContext = Substitute.For<ILambdaContext>(),
            RequestId = "test-request-id"
        };
    }

    [Fact]
    public async Task InvokeAsync_StopsOnCancellation() {
        var cts = new CancellationTokenSource();

        // Cancel immediately — GetNextInvocation should see the token cancelled
        cts.Cancel();

        await _engine.InvokeAsync(cts.Token);

        // Should not have called GetNextInvocation since token was already cancelled
        await _serverProxy.DidNotReceiveWithAnyArgs()
            .GetNextInvocation(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_ProcessesSingleInvocation() {
        var cts = new CancellationTokenSource();
        var invocation = CreateInvocationData();
        var callCount = 0;

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns(callInfo => {
                callCount++;
                if (callCount > 1) {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
                return Task.FromResult(invocation);
            });

        var executionContext = Substitute.For<IExecutionContext>();
        var executionResponse = Substitute.For<IExecutionResponse>();
        executionContext.Response.Returns(executionResponse);

        _requestMapper.CreateExecutionContext(
            Arg.Any<IServiceProvider>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<APIGatewayHttpApiV2ProxyRequest>(),
            Arg.Any<ResponseStream>(),
            Arg.Any<MemoryStream>(),
            Arg.Any<IMetricLogger>()
        ).Returns(executionContext);

        var chain = Substitute.For<IExecutionChain>();
        chain.Next().Returns(Task.CompletedTask);
        _middlewareService.GetExecutionChain(executionContext).Returns(chain);

        await _engine.InvokeAsync(cts.Token);

        Assert.Equal(2, callCount);
        await chain.Received(1).Next();
        _lambdaContextAccessor.Received().Context = invocation.LambdaContext;
    }

    [Fact]
    public async Task InvokeAsync_SetsLambdaContext() {
        var cts = new CancellationTokenSource();
        var invocation = CreateInvocationData();
        var callCount = 0;

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns(callInfo => {
                callCount++;
                if (callCount > 1) {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
                return Task.FromResult(invocation);
            });

        var executionContext = Substitute.For<IExecutionContext>();
        var executionResponse = Substitute.For<IExecutionResponse>();
        executionContext.Response.Returns(executionResponse);

        _requestMapper.CreateExecutionContext(
            Arg.Any<IServiceProvider>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<APIGatewayHttpApiV2ProxyRequest>(),
            Arg.Any<ResponseStream>(),
            Arg.Any<MemoryStream>(),
            Arg.Any<IMetricLogger>()
        ).Returns(executionContext);

        var chain = Substitute.For<IExecutionChain>();
        chain.Next().Returns(Task.CompletedTask);
        _middlewareService.GetExecutionChain(executionContext).Returns(chain);

        await _engine.InvokeAsync(cts.Token);

        _lambdaContextAccessor.Received().Context = invocation.LambdaContext;
    }

    [Fact]
    public async Task InvokeAsync_HandlesMiddlewareException_WhenResponseNotStarted() {
        var cts = new CancellationTokenSource();
        var invocation = CreateInvocationData();
        var callCount = 0;

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns(callInfo => {
                callCount++;
                if (callCount > 1) {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
                return Task.FromResult(invocation);
            });

        var executionContext = Substitute.For<IExecutionContext>();
        var executionResponse = Substitute.For<IExecutionResponse>();
        executionContext.Response.Returns(executionResponse);

        _requestMapper.CreateExecutionContext(
            Arg.Any<IServiceProvider>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<APIGatewayHttpApiV2ProxyRequest>(),
            Arg.Any<ResponseStream>(),
            Arg.Any<MemoryStream>(),
            Arg.Any<IMetricLogger>()
        ).Returns(executionContext);

        var chain = Substitute.For<IExecutionChain>();
        chain.Next().Returns(Task.FromException(new InvalidOperationException("handler error")));
        _middlewareService.GetExecutionChain(executionContext).Returns(chain);

        // Should not throw — errors are caught and logged
        await _engine.InvokeAsync(cts.Token);

        // Engine should still process (it logged the error and continued the loop)
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// The engine ran requests without telling <see cref="IRequestLogger"/> anything at all. Every
    /// web transport calls it, and it is the seam the request lifecycle is observed through — so
    /// the streaming runtime produced no request logging whatsoever.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReportsTheRequestLifecycle() {
        var executionContext = ArrangeSingleInvocation(Task.CompletedTask);

        await _engine.InvokeAsync(_cancellation.Token);

        _requestLogger.Received(1).RequestBegin(executionContext);
        _requestLogger.Received(1).RequestEnd(executionContext);
    }

    /// <summary>
    /// A failed request is still a request: it gets an end, and its exception is reported against
    /// the context it belongs to rather than swallowed into the retry loop.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReportsAFailedRequestAgainstItsContext() {
        var failure = new InvalidOperationException("handler error");
        var executionContext = ArrangeSingleInvocation(Task.FromException(failure));

        await _engine.InvokeAsync(_cancellation.Token);

        _requestLogger.Received(1).RequestFailed(executionContext, failure);
        _requestLogger.Received(1).RequestEnd(executionContext);
    }

    /// <summary>
    /// Dispose is what writes the EMF line. Recording and disposing only on the success path meant
    /// a failed invocation emitted no metrics at all.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FlushesMetricsWhenTheRequestFails() {
        ArrangeSingleInvocation(Task.FromException(new InvalidOperationException("handler error")));

        await _engine.InvokeAsync(_cancellation.Token);

        _metricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
        _metricLogger.Received(1).Dispose();
    }

    [Fact]
    public async Task InvokeAsync_GracefulShutdown_OnOperationCancelled() {
        var cts = new CancellationTokenSource();

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns<Task<InvocationData>>(callInfo => {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        // Should complete without throwing
        await _engine.InvokeAsync(cts.Token);
    }
}
