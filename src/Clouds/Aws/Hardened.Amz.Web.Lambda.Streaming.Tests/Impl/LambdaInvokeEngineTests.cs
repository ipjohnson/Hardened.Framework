using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Execution;
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
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly LambdaInvokeEngine _engine;

    public LambdaInvokeEngineTests() {
        _serviceProvider = CreateServiceProvider();
        _serverProxy = Substitute.For<ILambdaServerProxy>();
        _middlewareService = Substitute.For<IMiddlewareService>();
        _requestMapper = Substitute.For<IStreamingRequestMapper>();
        _metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
        _lambdaContextAccessor = Substitute.For<ILambdaContextAccessor>();

        _metricLoggerProvider.CreateLogger(Arg.Any<string>())
            .Returns(Substitute.For<IMetricLogger>());

        _engine = new LambdaInvokeEngine(
            _serviceProvider,
            _serverProxy,
            _middlewareService,
            _requestMapper,
            _metricLoggerProvider,
            _lambdaContextAccessor);
    }

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
