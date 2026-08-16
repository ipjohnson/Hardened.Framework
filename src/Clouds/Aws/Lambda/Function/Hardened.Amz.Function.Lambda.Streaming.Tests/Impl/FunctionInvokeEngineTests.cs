using Amazon.Lambda.Core;
using Hardened.Amz.Function.Lambda.Streaming.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Streaming.Tests.Impl;

/// <summary>
/// The invoke loop for the streaming function runtime.
/// </summary>
/// <remarks>
/// This engine had no tests, and it showed. It created a metric logger and passed it straight into
/// the execution context with no local to hold it, so it could never dispose it — and
/// <c>EmbeddedMetricLogger.Dispose</c> is what writes the EMF line, so the runtime emitted no
/// metrics at all. It never recorded <c>TotalRequestDuration</c>. And it never called
/// <see cref="IRequestLogger"/>, which every web transport does and which is the seam the request
/// lifecycle is observed through.
/// </remarks>
public class FunctionInvokeEngineTests {
    private readonly IFunctionServerProxy _serverProxy = Substitute.For<IFunctionServerProxy>();
    private readonly IMiddlewareService _middlewareService = Substitute.For<IMiddlewareService>();
    private readonly IStreamingFunctionRequestMapper _requestMapper = Substitute.For<IStreamingFunctionRequestMapper>();
    private readonly IMetricLoggerProvider _metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
    private readonly IMetricLogger _metricLogger = Substitute.For<IMetricLogger>();
    private readonly ILambdaContextAccessor _lambdaContextAccessor = Substitute.For<ILambdaContextAccessor>();
    private readonly IRequestLogger _requestLogger = Substitute.For<IRequestLogger>();
    private readonly FunctionInvokeEngine _engine;

    private CancellationTokenSource _cancellation = new();

    public FunctionInvokeEngineTests() {
        _metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(_metricLogger);

        _engine = new FunctionInvokeEngine(
            new ServiceCollection().BuildServiceProvider(),
            _serverProxy,
            _middlewareService,
            _requestMapper,
            _metricLoggerProvider,
            _lambdaContextAccessor,
            _requestLogger,
            NullLogger<FunctionInvokeEngine>.Instance);
    }

    /// <summary>
    /// One invocation, then a cancellation so the loop exits. Returns the context the mapper hands
    /// the engine, which is what the assertions are written against.
    /// </summary>
    private IExecutionContext ArrangeSingleInvocation(Task chainResult) {
        var cts = new CancellationTokenSource();
        var callCount = 0;

        _serverProxy.GetNextInvocation(Arg.Any<CancellationToken>())
            .Returns(_ => {
                callCount++;

                if (callCount > 1) {
                    cts.Cancel();

                    throw new OperationCanceledException(cts.Token);
                }

                return Task.FromResult(new InvocationData {
                    Body = new MemoryStream(),
                    LambdaContext = Substitute.For<ILambdaContext>(),
                    RequestId = "test-request-id"
                });
            });

        var executionContext = Substitute.For<IExecutionContext>();
        executionContext.Response.Returns(Substitute.For<IExecutionResponse>());

        _requestMapper.CreateExecutionContext(
            Arg.Any<IServiceProvider>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<InvocationData>(),
            Arg.Any<ResponseStream>(),
            Arg.Any<IMetricLogger>()
        ).Returns(executionContext);

        var chain = Substitute.For<IExecutionChain>();
        chain.Next().Returns(chainResult);
        _middlewareService.GetExecutionChain(executionContext).Returns(chain);

        _cancellation = cts;

        return executionContext;
    }

    [Fact]
    public async Task InvokeAsync_ReportsTheRequestLifecycle() {
        var executionContext = ArrangeSingleInvocation(Task.CompletedTask);

        await _engine.InvokeAsync(_cancellation.Token);

        _requestLogger.Received(1).RequestBegin(executionContext);
        _requestLogger.Received(1).RequestEnd(executionContext);
    }

    /// <summary>
    /// A failed request is still a request: it gets an end, and its exception is reported against
    /// the context it belongs to rather than written to stderr and lost.
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
    /// Dispose is what writes the EMF line, and the engine held no reference it could dispose.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_RecordsAndFlushesMetrics() {
        ArrangeSingleInvocation(Task.CompletedTask);

        await _engine.InvokeAsync(_cancellation.Token);

        _metricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
        _metricLogger.Received(1).Dispose();
    }

    [Fact]
    public async Task InvokeAsync_FlushesMetricsWhenTheRequestFails() {
        ArrangeSingleInvocation(Task.FromException(new InvalidOperationException("handler error")));

        await _engine.InvokeAsync(_cancellation.Token);

        _metricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
        _metricLogger.Received(1).Dispose();
    }

    /// <summary>
    /// A cancelled token means shutdown, not a failed request: nothing should be reported against a
    /// context that was never created.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_StopsOnCancellationWithoutReportingAFailure() {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await _engine.InvokeAsync(cts.Token);

        await _serverProxy.DidNotReceiveWithAnyArgs().GetNextInvocation(Arg.Any<CancellationToken>());
        _requestLogger.DidNotReceiveWithAnyArgs().RequestFailed(Arg.Any<IExecutionContext>(), Arg.Any<Exception>());
    }
}
