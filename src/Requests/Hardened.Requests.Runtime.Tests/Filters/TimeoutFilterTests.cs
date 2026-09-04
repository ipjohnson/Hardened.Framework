using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Metrics;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// What <see cref="TimeoutFilter"/> puts on the context for the span it wraps, and what it puts
/// back.
/// </summary>
/// <remarks>
/// Every one of these builds a real <c>ExecutionChain</c> through <c>Pipeline.Chain</c>, for the
/// reason <c>RetryFilterTests</c> gives: a substituted chain has a re-runnable <c>Next</c> and the
/// real one advances an index.
/// </remarks>
public class TimeoutFilterTests {

    /// <summary>A budget short enough to expire during a test, but not so short it races.</summary>
    private const int ShortBudget = 30;

    /// <summary>Longer than any test here takes, so the deadline never fires on its own.</summary>
    private const int LongBudget = 60_000;

    [Fact]
    public async Task TheChainRunsOnADeadlineTokenRatherThanTheTransports() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        CancellationToken observed = default;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(chain => {
                observed = chain.Context.CancellationToken;

                return Task.CompletedTask;
            })).Next();

        Assert.NotEqual(transport.Token, observed);
        Assert.True(observed.CanBeCanceled);
    }

    /// <summary>
    /// The whole feature: work that outlives the budget is cancelled. A handler declaring a
    /// <c>CancellationToken</c> parameter is handed this same token as the request is bound, which
    /// is why the filter has to sit ahead of serialization.
    /// </summary>
    [Fact]
    public async Task WorkThatOutlivesTheBudgetIsCancelled() {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(ShortBudget),
            new Pipeline.Inline(inner =>
                Task.Delay(Timeout.Infinite, inner.Context.CancellationToken)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());
    }

    /// <summary>
    /// The restore. <c>ConditionalGetFilter</c> flushes its held-back body and
    /// <c>ResponseCacheFilter</c> stores its entry after the inner chain returns, both on
    /// <c>context.CancellationToken</c> - so a request that spent its whole budget must not leave a
    /// cancelled token behind for them.
    /// </summary>
    [Fact]
    public async Task TheTransportTokenIsBackWhenTheFilterReturns() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        await Pipeline.Chain(context, new TimeoutFilter(LongBudget)).Next();

        Assert.Equal(transport.Token, context.CancellationToken);
    }

    [Fact]
    public async Task TheTransportTokenIsBackAfterTheDeadlineFired() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(ShortBudget),
            new Pipeline.Inline(inner =>
                Task.Delay(Timeout.Infinite, inner.Context.CancellationToken)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());

        Assert.Equal(transport.Token, context.CancellationToken);
        Assert.False(context.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task TheTransportTokenIsBackAfterTheChainThrew() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(_ => throw new InvalidOperationException("the handler failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => chain.Next());

        Assert.Equal(transport.Token, context.CancellationToken);
    }

    /// <summary>
    /// Linked, so a client that hangs up still stops the work. A deadline that replaced the
    /// transport's token instead of linking from it would make a disconnect unobservable for
    /// exactly the operations that declared a budget.
    /// </summary>
    [Fact]
    public async Task TheTransportsOwnCancellationStillReachesTheChain() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(async inner => {
                await transport.CancelAsync();

                await Task.Delay(Timeout.Infinite, inner.Context.CancellationToken);
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());
    }

    [Fact]
    public async Task ADeadlineThatFiredIsCounted() {
        var metrics = Substitute.For<IMetricLogger>();
        var context = Pipeline.Context(metrics: metrics);

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(ShortBudget),
            new Pipeline.Inline(inner =>
                Task.Delay(Timeout.Infinite, inner.Context.CancellationToken)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());

        metrics.Received(1).Record(RequestMetrics.RequestTimedOut, 1);
    }

    /// <summary>
    /// A client closing a tab cancels the same linked token as a budget running out. Counting that
    /// here would report the slow handler this metric exists to find every time somebody navigated
    /// away.
    /// </summary>
    [Fact]
    public async Task AClientDisconnectIsNotCountedAsATimeout() {
        using var transport = new CancellationTokenSource();

        var metrics = Substitute.For<IMetricLogger>();
        var context = Pipeline.Cancellable(transport.Token, metrics: metrics);

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(async inner => {
                await transport.CancelAsync();

                await Task.Delay(Timeout.Infinite, inner.Context.CancellationToken);
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());

        metrics.DidNotReceive().Record(RequestMetrics.RequestTimedOut, Arg.Any<double>());
    }

    [Fact]
    public async Task ARequestThatFinishedInTimeRecordsNothing() {
        var metrics = Substitute.For<IMetricLogger>();
        var context = Pipeline.Context(metrics: metrics);

        await Pipeline.Chain(context, new TimeoutFilter(LongBudget)).Next();

        metrics.DidNotReceive().Record(RequestMetrics.RequestTimedOut, Arg.Any<double>());
    }
}
