using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Execution;

/// <summary>
/// <see cref="LambdaExecutionContext.Clone"/> is what a batch filter forks a record onto. Every
/// argument it drops silently is a service or a handler the forked record then cannot see.
/// </summary>
public class LambdaExecutionContextTests {

    /// <summary>
    /// Returned as <see cref="IExecutionContext"/> on purpose: the concrete
    /// <see cref="LambdaExecutionContext.Clone"/> re-declares the interface's four parameters
    /// without their defaults, so only a caller holding the interface can omit them — and a batch
    /// filter, which is the only caller that matters, holds the interface.
    /// </summary>
    private static IExecutionContext Create() {
        return TestExecutionContext.Create(new MemoryStream(), new MemoryStream());
    }

    [Fact]
    public void CloningWithoutArgumentsKeepsTheOriginalRequestAndResponse() {
        var context = Create();

        var clone = context.Clone();

        Assert.Same(context.Request, clone.Request);
        Assert.Same(context.Response, clone.Response);
        Assert.Same(context.RequestServices, clone.RequestServices);
        Assert.Same(context.RootServiceProvider, clone.RootServiceProvider);
    }

    [Fact]
    public void CloningReplacesOnlyWhatWasSupplied() {
        var context = Create();

        var request = new LambdaExecutionRequest(
            "Invoke", "Other", new MemoryStream(), new Dictionary<string, StringValues>());

        var clone = context.Clone(request);

        Assert.Same(request, clone.Request);
        Assert.Same(context.Response, clone.Response);
    }

    [Fact]
    public void CloningReplacesTheResponseWhenOneIsSupplied() {
        var context = Create();
        var response = new LambdaExecutionResponse(new MemoryStream(), new HeaderCollectionStringValues());

        var clone = context.Clone(response: response);

        Assert.Same(response, clone.Response);
        Assert.Same(context.Request, clone.Request);
    }

    /// <summary>
    /// The handler and its info ride along on a clone. A forked record that lost them would be
    /// re-resolved, or run with no handler at all.
    /// </summary>
    [Fact]
    public void TheHandlerAndItsInfoSurviveAClone() {
        var context = Create();
        var handler = new object();
        var handlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();

        context.HandlerInstance = handler;
        context.HandlerInfo = handlerInfo;

        var clone = context.Clone();

        Assert.Same(handler, clone.HandlerInstance);
        Assert.Same(handlerInfo, clone.HandlerInfo);
    }

    [Fact]
    public void TheStartTimeIsCarriedForwardSoADurationSpansTheWholeBatch() {
        var context = Create();

        Assert.Equal(context.StartTime, context.Clone().StartTime);
    }

    [Fact]
    public void CloningSwapsTheMetricLoggerWhenOneIsSupplied() {
        var context = Create();
        var metrics = Substitute.For<IMetricLogger>();

        Assert.Same(metrics, context.Clone(metricLogger: metrics).RequestMetrics);
        Assert.Same(context.RequestMetrics, context.Clone().RequestMetrics);
    }

    [Fact]
    public void CloningSwapsTheRequestScopeWhenOneIsSupplied() {
        var context = Create();
        var scope = new StubServiceProvider();

        Assert.Same(scope, context.Clone(serviceProvider: scope).RequestServices);
    }

    [Fact]
    public void ALambdaInvocationCarriesNoCancellationToken() {
        Assert.Equal(CancellationToken.None, Create().CancellationToken);
    }
}
