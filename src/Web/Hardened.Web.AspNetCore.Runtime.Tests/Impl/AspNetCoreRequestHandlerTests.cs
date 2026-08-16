using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// What the handler does around the chain.
///
/// Request logging and the duration metric are the part every other host already had —
/// <c>ApiGatewayEventProcessor</c> on Lambda, <c>TestWebApp</c> in the test harness,
/// <c>HardenedHttpApplication</c> on Kestrel — and this host did not, so an ASP.NET-hosted
/// application produced no begin, no end and no <c>TotalRequestDuration</c>.
/// </summary>
public class AspNetCoreRequestHandlerTests {

    [Fact]
    public async Task HandleRequest_LogsTheRequestBeginningAndEnding() {
        var harness = new Harness();

        await harness.Handler.HandleRequest(harness.HttpContext, _ => Task.CompletedTask);

        harness.RequestLogger.Received(1).RequestBegin(Arg.Any<IExecutionContext>());
        harness.RequestLogger.Received(1).RequestEnd(Arg.Any<IExecutionContext>());
    }

    [Fact]
    public async Task HandleRequest_RecordsTheTotalRequestDuration() {
        var harness = new Harness();

        await harness.Handler.HandleRequest(harness.HttpContext, _ => Task.CompletedTask);

        harness.MetricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
    }

    [Fact]
    public async Task HandleRequest_RunsTheExecutionChain() {
        var harness = new Harness();

        await harness.Handler.HandleRequest(harness.HttpContext, _ => Task.CompletedTask);

        await harness.Chain.Received(1).Next();
    }

    /// <summary>
    /// A chain that produced nothing means Hardened did not handle the request, so it is passed
    /// to whatever comes next in the ASP.NET pipeline.
    /// </summary>
    [Fact]
    public async Task HandleRequest_InvokesTheNextDelegateWhenTheResponseHasNotStarted() {
        var harness = new Harness();
        var nextInvoked = false;

        await harness.Handler.HandleRequest(harness.HttpContext, _ => {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        Assert.True(nextInvoked);
    }

    /// <summary>
    /// Once the chain has written a response there is nothing for the rest of the pipeline to do,
    /// and handing it on would let the terminal delegate overwrite the status.
    /// </summary>
    [Fact]
    public async Task HandleRequest_SkipsTheNextDelegateWhenTheResponseHasStarted() {
        var harness = new Harness(startResponse: true);
        var nextInvoked = false;

        await harness.Handler.HandleRequest(harness.HttpContext, _ => {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        Assert.False(nextInvoked);
    }

    /// <summary>
    /// The four ways the chain answers with no body, each of which used to fall through to the
    /// terminal delegate and come back as ASP.NET's 404.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the cases the shortener app's two-host diff found: <c>DELETE</c> returning 200,
    /// a wrong verb returning 405 with an <c>Allow</c> header, a <c>HEAD</c>, and a 302 redirect.
    /// Each really had happened — the DELETE deleted — and each was reported to the caller as a
    /// resource that does not exist.
    /// </para>
    /// <para>
    /// They are expressed as what the chain leaves on the context rather than as whole requests,
    /// because that is the boundary this class owns. A status covers the 405, the redirect and any
    /// handler that sets one; <c>HandlerInfo</c> covers a route that matched and wrote nothing,
    /// which is the signal the old check had no equivalent of at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task HandleRequest_SkipsTheNextDelegateWhenTheChainSetAStatusWithNoBody() {
        var harness = new Harness(chainEffect: context => context.Response.Status = 204);
        var nextInvoked = false;

        await harness.Handler.HandleRequest(harness.HttpContext, _ => {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        Assert.False(nextInvoked);
        Assert.Equal(204, harness.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_SkipsTheNextDelegateWhenRoutingSelectedAHandler() {
        var harness = new Harness(chainEffect: context =>
            context.HandlerInfo = Substitute.For<IExecutionRequestHandlerInfo>());
        var nextInvoked = false;

        await harness.Handler.HandleRequest(harness.HttpContext, _ => {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        Assert.False(nextInvoked);
    }

    [Fact]
    public async Task HandleRequest_SkipsTheNextDelegateWhenTheChainLeftAResponseValue() {
        var harness = new Harness(chainEffect: context => context.Response.ResponseValue = "value");
        var nextInvoked = false;

        await harness.Handler.HandleRequest(harness.HttpContext, _ => {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        Assert.False(nextInvoked);
    }

    private class Harness {
        public Harness(bool startResponse = false, Action<IExecutionContext>? chainEffect = null) {
            RequestLogger = Substitute.For<IRequestLogger>();
            MetricLogger = Substitute.For<IMetricLogger>();
            Chain = Substitute.For<IExecutionChain>();

            var metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
            metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(MetricLogger);

            var middlewareService = Substitute.For<IMiddlewareService>();

            // The context is built inside HandleRequest, so it is captured on the way past rather
            // than handed in — chainEffect stands in for whatever the real chain would have left
            // on it.
            middlewareService.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
                ExecutionContext = callInfo.Arg<IExecutionContext>();

                return Chain;
            });

            Chain.Next().Returns(_ => {
                chainEffect?.Invoke(ExecutionContext!);

                return Task.CompletedTask;
            });

            // AspNetExecutionContext resolves IKnownServices out of RequestServices as it is built.
            var services = new ServiceCollection();
            services.AddSingleton(Substitute.For<IKnownServices>());

            HttpContext = StartableResponseContext.Create(
                services.BuildServiceProvider(), out var start);

            if (startResponse) {
                start();
            }

            Handler = new AspNetCoreRequestHandler(metricLoggerProvider, middlewareService, RequestLogger);
        }

        public IRequestLogger RequestLogger { get; }

        public IMetricLogger MetricLogger { get; }

        public IExecutionChain Chain { get; }

        public IExecutionContext? ExecutionContext { get; private set; }

        public HttpContext HttpContext { get; }

        public AspNetCoreRequestHandler Handler { get; }
    }
}
