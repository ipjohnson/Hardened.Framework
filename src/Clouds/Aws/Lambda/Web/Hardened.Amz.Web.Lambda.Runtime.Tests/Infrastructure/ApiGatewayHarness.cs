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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// Drives a real <see cref="ApiGatewayEventProcessor"/> over a real payload and hands the test the
/// <see cref="IExecutionContext"/> the middleware chain was given.
///
/// <para>
/// The mapping types — <c>ApiGatewayV2ExecutionRequest</c> and <c>ApiGatewayV2ExecutionContext</c> —
/// are <c>internal</c>, and the processor is the only thing that builds them. Going through it is
/// therefore not a compromise: it is the path a real invocation takes, so a test that passes here
/// says the payload a handler sees is the payload API Gateway sent.
/// </para>
/// </summary>
internal sealed class ApiGatewayHarness {
    private readonly ServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService = Substitute.For<IMiddlewareService>();

    public ApiGatewayHarness() {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        RequestLogger = Substitute.For<IRequestLogger>();
        KnownServices = Substitute.For<IKnownServices>();
        MetricLogger = Substitute.For<IMetricLogger>();
        LambdaContextAccessor = new LambdaContextAccessor();
        ProxyRequestContextAccessor = new ProxyRequestContextAccessor();

        var metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
        metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(MetricLogger);

        Processor = new ApiGatewayEventProcessor(
            _serviceProvider,
            _middlewareService,
            new MemoryStreamPool(),
            RequestLogger,
            NullLogger<ApiGatewayEventProcessor>.Instance,
            metricLoggerProvider,
            KnownServices,
            LambdaContextAccessor,
            new StringBuilderPool(),
            ProxyRequestContextAccessor);
    }

    public ApiGatewayEventProcessor Processor { get; }

    public IRequestLogger RequestLogger { get; }

    public IKnownServices KnownServices { get; }

    public IMetricLogger MetricLogger { get; }

    public ILambdaContextAccessor LambdaContextAccessor { get; }

    public IProxyRequestContextAccessor ProxyRequestContextAccessor { get; }

    /// <summary>The context the middleware chain ran against, available after <see cref="Process"/>.</summary>
    public IExecutionContext ExecutionContext =>
        _capturedContext ?? throw new InvalidOperationException("Process has not run yet.");

    private IExecutionContext? _capturedContext;

    /// <summary>
    /// Runs <paramref name="request"/> through the processor, calling <paramref name="handler"/>
    /// where the middleware chain would run.
    /// </summary>
    public Task<APIGatewayHttpApiV2ProxyResponse> Process(
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

        return Processor.Process(request, lambdaContext ?? Substitute.For<ILambdaContext>());
    }

    /// <summary>
    /// An API Gateway HTTP API (payload format 2.0) event, with every field the mapper reads
    /// present and defaulted to something innocuous.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyRequest Event(
        string method = "GET",
        string rawPath = "/orders",
        string? stage = null,
        string? body = null,
        bool isBase64Encoded = false,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string>? queryStringParameters = null,
        string[]? cookies = null) =>
        new() {
            RawPath = rawPath,
            Body = body,
            IsBase64Encoded = isBase64Encoded,
            Headers = headers ?? new Dictionary<string, string>(),
            QueryStringParameters = queryStringParameters,
            Cookies = cookies ?? Array.Empty<string>(),
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                Stage = stage,
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                    Method = method,
                    Path = rawPath
                }
            }
        };
}
