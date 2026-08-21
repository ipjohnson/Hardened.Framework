using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// The context an ASP.NET-hosted request runs on.
/// </summary>
/// <remarks>
/// <para>
/// CI measured this at <b>23% line / 25% branch</b> — the lowest of anything in a shipped runtime
/// assembly, in the type every ASP.NET-hosted application builds per request. The conformance suites
/// cover the request and response adapters beside it; the context itself was reached only far enough
/// to construct it.
/// </para>
/// <para>
/// <b><see cref="AForkIsTheSameCaller"/> and <see cref="AForkReportsOneCorrelationId"/> are why this
/// file exists.</b> <c>Clone</c> is what <c>RetryFilter</c> calls for every attempt after the first,
/// and its two comments — "the reference, not a copy: a fork is the same caller" and "the same
/// request, so it reports one id rather than two" — are claims about what a retried request looks
/// like in an audit log. Neither had been executed on this host.
/// </para>
/// </remarks>
public class AspNetExecutionContextTests {

    private static AspNetExecutionContext Context(out DefaultHttpContext httpContext) {
        var services = new ServiceCollection();

        services.AddSingleton(Substitute.For<IKnownServices>());

        httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/pets";

        return new AspNetExecutionContext(httpContext, Substitute.For<IMetricLogger>());
    }

    private static AspNetExecutionContext Context() => Context(out _);

    #region pass-throughs to the HttpContext

    /// <summary>
    /// Both providers are the request's scope. A root provider that outlived the request would let
    /// a singleton capture a scoped service.
    /// </summary>
    [Fact]
    public void BothServiceProvidersAreTheRequestScope() {
        var context = Context(out var httpContext);

        Assert.Same(httpContext.RequestServices, context.RequestServices);
        Assert.Same(httpContext.RequestServices, context.RootServiceProvider);
    }

    /// <summary>
    /// The token is the connection's, so a handler that honours cancellation stops when the client
    /// hangs up rather than running to completion writing to a closed socket.
    /// </summary>
    [Fact]
    public void TheCancellationTokenTracksRequestAborted() {
        using var aborted = new CancellationTokenSource();

        var context = Context(out var httpContext);

        httpContext.RequestAborted = aborted.Token;

        Assert.Equal(aborted.Token, context.CancellationToken);

        aborted.Cancel();

        Assert.True(context.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void TheRequestAndResponseWrapTheHttpContexts() {
        var context = Context(out var httpContext);

        Assert.Equal(httpContext.Request.Method, context.Request.Method);
        Assert.Equal(httpContext.Request.Path.Value, context.Request.Path);
    }

    [Fact]
    public void KnownServicesComesFromTheContainer() {
        Assert.NotNull(Context().KnownServices);
    }

    [Fact]
    public void TheStartTimeIsTaken() {
        Assert.True(Context().StartTime.GetElapsedMilliseconds() >= 0);
    }

    /// <summary>
    /// Hardened's own principal, not <c>HttpContext.User</c> — bridging the two is an opt-in
    /// adapter, so moving a handler between hosts does not change how it authenticates.
    /// </summary>
    [Fact]
    public void ThePrincipalStartsAnonymous() {
        Assert.Same(AnonymousCallerPrincipal.Instance, Context().CallerPrincipal);
        Assert.False(Context().CallerPrincipal.IsAuthenticated);
    }

    [Fact]
    public void ACorrelationIdIsProducedWhenNoneWasSet() {
        Assert.False(string.IsNullOrEmpty(Context().CorrelationId));
    }

    [Fact]
    public void TheCorrelationIdIsStableWithinOneContext() {
        var context = Context();

        Assert.Equal(context.CorrelationId, context.CorrelationId);
    }

    #endregion

    #region forking

    private static IExecutionContext Fork(IExecutionContext context) =>
        context.Clone(null, null, null, null);

    [Fact]
    public void AForkIsADifferentContext() {
        var context = Context();

        Assert.NotSame(context, Fork(context));
    }

    /// <summary>
    /// A null argument keeps the current value — which is the whole shape <c>RetryFilter</c> relies
    /// on, since it forks without replacing anything.
    /// </summary>
    [Fact]
    public void AForkKeepsTheRequestAndResponseWhenNoneAreSupplied() {
        var context = Context();
        var fork = Fork(context);

        Assert.Same(context.Request, fork.Request);
        Assert.Same(context.Response, fork.Response);
    }

    [Fact]
    public void AForkTakesASuppliedRequestAndResponse() {
        var context = Context();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();

        var fork = context.Clone(request, response, null, null);

        Assert.Same(request, fork.Request);
        Assert.Same(response, fork.Response);
    }

    [Fact]
    public void AForkTakesASuppliedMetricLogger() {
        var context = Context();
        var metrics = Substitute.For<IMetricLogger>();

        Assert.Same(metrics, context.Clone(null, null, null, metrics).RequestMetrics);
    }

    [Fact]
    public void AForkKeepsTheMetricLoggerWhenNoneIsSupplied() {
        var context = Context();

        Assert.Same(context.RequestMetrics, Fork(context).RequestMetrics);
    }

    /// <summary>
    /// The reference, not a copy. A retried attempt is the same caller, and an authorization filter
    /// re-running on the fork has to reach the same answer.
    /// </summary>
    [Fact]
    public void AForkIsTheSameCaller() {
        var context = Context();
        var caller = new CallerPrincipal("bearer", ["pets:read"]);

        context.CallerPrincipal = caller;

        Assert.Same(caller, Fork(context).CallerPrincipal);
    }

    /// <summary>
    /// One id rather than two. A retried request that reported a fresh correlation id per attempt
    /// would look like several requests in a log, which is exactly the thing a correlation id is
    /// for.
    /// </summary>
    [Fact]
    public void AForkReportsOneCorrelationId() {
        var context = Context();

        Assert.Equal(context.CorrelationId, Fork(context).CorrelationId);
    }

    [Fact]
    public void AForkKeepsTheHandlerInstanceAndInfo() {
        var context = Context();
        var handler = new object();
        var info = Substitute.For<IExecutionRequestHandlerInfo>();

        context.HandlerInstance = handler;
        context.HandlerInfo = info;

        var fork = Fork(context);

        Assert.Same(handler, fork.HandlerInstance);
        Assert.Same(info, fork.HandlerInfo);
    }

    /// <summary>
    /// The fork starts when the request did, so a timing filter on it measures the whole request
    /// rather than the part after the fork.
    /// </summary>
    /// <remarks>
    /// The timestamps are compared, not two readings of them. Reading each one's elapsed time asks
    /// what the clock said at two different moments and then asserts the answers round to the same
    /// millisecond - which holds only if nothing happened in between, and is a claim about the
    /// machine rather than about the fork. <c>MachineTimestamp</c> is a struct over one tick count,
    /// so comparing the values says exactly what this test means and says it exactly.
    /// </remarks>
    [Fact]
    public void AForkKeepsTheStartTime() {
        var context = Context();

        Assert.Equal(context.StartTime, Fork(context).StartTime);
    }

    /// <summary>
    /// The fork still reaches the same connection — its services and cancellation come from the
    /// same <c>HttpContext</c>, so a handler on the fork sees the client hang up.
    /// </summary>
    [Fact]
    public void AForkStillSeesTheSameConnection() {
        using var aborted = new CancellationTokenSource();

        var context = Context(out var httpContext);

        httpContext.RequestAborted = aborted.Token;

        var fork = Fork(context);

        Assert.Same(httpContext.RequestServices, fork.RequestServices);
        Assert.Equal(aborted.Token, fork.CancellationToken);
    }

    /// <summary>
    /// Forking a fork keeps the caller and the id, which is what three retry attempts actually do.
    /// </summary>
    [Fact]
    public void ForkingAForkStillCarriesTheCallerAndId() {
        var context = Context();
        var caller = new CallerPrincipal("bearer", ["pets:read"]);

        context.CallerPrincipal = caller;

        var second = Fork(Fork(context));

        Assert.Same(caller, second.CallerPrincipal);
        Assert.Equal(context.CorrelationId, second.CorrelationId);
    }

    #endregion
}
