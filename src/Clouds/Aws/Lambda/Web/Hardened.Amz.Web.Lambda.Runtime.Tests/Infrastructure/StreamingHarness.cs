using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// Drives a real <see cref="StreamingEventProcessor"/> over a real payload, with the stream opened
/// into a <see cref="CapturingResponseStreamFactory"/>, and hands the test the
/// <see cref="IExecutionContext"/> the middleware chain was given.
/// </summary>
internal sealed class StreamingHarness {
    private readonly ServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService = Substitute.For<IMiddlewareService>();

    public StreamingHarness() {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        RequestLogger = Substitute.For<IRequestLogger>();
        KnownServices = Substitute.For<IKnownServices>();
        MetricLogger = Substitute.For<IMetricLogger>();
        LambdaContextAccessor = new LambdaContextAccessor();
        ProxyRequestContextAccessor = new ProxyRequestContextAccessor();

        var metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
        metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(MetricLogger);

        Processor = new StreamingEventProcessor(
            _serviceProvider,
            _middlewareService,
            new MemoryStreamPool(),
            RequestLogger,
            metricLoggerProvider,
            KnownServices,
            LambdaContextAccessor,
            new StringBuilderPool(),
            ProxyRequestContextAccessor);
    }

    public StreamingEventProcessor Processor { get; }

    public CapturingResponseStreamFactory Streams { get; } = new();

    public IRequestLogger RequestLogger { get; }

    public IKnownServices KnownServices { get; }

    public IMetricLogger MetricLogger { get; }

    public ILambdaContextAccessor LambdaContextAccessor { get; }

    public IProxyRequestContextAccessor ProxyRequestContextAccessor { get; }

    /// <summary>Everything the pump wrote, as text.</summary>
    public string Body => Encoding.UTF8.GetString(Streams.Target.ToArray());

    /// <summary>The context the middleware chain ran against, available after <see cref="Process"/>.</summary>
    public IExecutionContext ExecutionContext =>
        _capturedContext ?? throw new InvalidOperationException("Process has not run yet.");

    private IExecutionContext? _capturedContext;

    public Task Process(
        APIGatewayHttpApiV2ProxyRequest request,
        Func<IExecutionContext, Task>? handler = null,
        ILambdaContext? lambdaContext = null) {
        _middlewareService.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
            _capturedContext = callInfo.Arg<IExecutionContext>();

            var chain = Substitute.For<IExecutionChain>();
            chain.Context.Returns(_capturedContext);
            chain.Next().Returns(_ =>
                handler == null ? Task.CompletedTask : handler(_capturedContext));

            return chain;
        });

        return Processor.Process(request, lambdaContext ?? Substitute.For<ILambdaContext>(), Streams);
    }
}
