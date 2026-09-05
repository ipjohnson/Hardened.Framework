using Hardened.Requests.Abstract.Timeouts;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// What a handler can learn about its budget, and what it costs when it does not ask.
/// </summary>
/// <remarks>
/// Read through <see cref="IRequestDeadline"/> rather than the <c>AsyncLocal</c> behind it, because
/// the accessor is the whole contract: a handler takes this in its constructor whatever its
/// lifetime, which is the reason the value is not on a scoped holder.
/// </remarks>
public class RequestDeadlineTests {

    /// <summary>Longer than any test here takes, so nothing expires on its own.</summary>
    private const int LongBudget = 60_000;

    /// <summary>Short enough to expire during a test, but not so short it races.</summary>
    private const int ShortBudget = 30;

    private static readonly IRequestDeadline Accessor = new RequestDeadline();

    [Fact]
    public async Task ABoundedHandlerReadsWhenItsBudgetRunsOut() {
        var context = Pipeline.Context();

        double remaining = 0;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(_ => {
                remaining = Accessor.Deadline!.Value.GetRemainingMilliseconds();

                return Task.CompletedTask;
            })).Next();

        // Bounded rather than exact: the deadline is taken as the filter starts the budget, so the
        // reading is a whole budget less however long the chain took to reach the handler.
        Assert.InRange(remaining, LongBudget - 5_000, LongBudget);
    }

    /// <summary>
    /// The budget is what the handler reads, not the request's whole elapsed time. A deadline
    /// measured from anywhere but the <c>CancelAfter</c> would disagree with the token enforcing it.
    /// </summary>
    [Fact]
    public async Task TheDeadlineIsAheadWhileTheBudgetHolds() {
        var context = Pipeline.Context();

        var future = false;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(_ => {
                future = Accessor.Deadline!.Value.Future;

                return Task.CompletedTask;
            })).Next();

        Assert.True(future);
    }

    /// <summary>
    /// The opt-out. Nothing else about the budget changes; only the publication is skipped, which
    /// is the <c>AsyncLocal</c> write and the execution-context copies after it.
    /// </summary>
    [Fact]
    public async Task DeadlineFalsePublishesNothing() {
        var context = Pipeline.Context();

        MachineTimestamp? observed = null;
        CancellationToken bounded = default;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget, publishDeadline: false),
            new Pipeline.Inline(chain => {
                observed = Accessor.Deadline;
                bounded = chain.Context.CancellationToken;

                return Task.CompletedTask;
            })).Next();

        Assert.Null(observed);

        // Both readings say the same thing, and neither is a token that looks live.
        Assert.False(Accessor.CancellationToken.CanBeCanceled);

        // The budget still applies; only the reading of it was declined.
        Assert.True(bounded.CanBeCanceled);
    }

    [Fact]
    public void AHandlerNoBudgetAppliesToReadsNothing() {
        Assert.Null(Accessor.Deadline);
        Assert.Equal(CancellationToken.None, Accessor.CancellationToken);
    }

    /// <summary>
    /// The published token is the one enforcing the budget, not a copy of the transport's. Passing
    /// it to an upstream call is what makes the deadline reach that call, so a token that merely
    /// looks cancellable would be the wrong one.
    /// </summary>
    [Fact]
    public async Task ThePublishedTokenIsTheOneTheBudgetCancels() {
        using var transport = new CancellationTokenSource();

        var context = Pipeline.Cancellable(transport.Token);

        CancellationToken published = default;
        CancellationToken installed = default;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(chain => {
                published = Accessor.CancellationToken;
                installed = chain.Context.CancellationToken;

                return Task.CompletedTask;
            })).Next();

        Assert.Equal(installed, published);
        Assert.NotEqual(transport.Token, published);
    }

    /// <summary>
    /// The whole point of carrying the token: work started on it is abandoned when the budget runs
    /// out, for a handler that reached it through the accessor rather than a parameter.
    /// </summary>
    [Fact]
    public async Task WorkStartedOnThePublishedTokenIsCancelledByTheBudget() {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(
            context,
            new TimeoutFilter(ShortBudget),
            new Pipeline.Inline(_ => Task.Delay(Timeout.Infinite, Accessor.CancellationToken)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chain.Next());
    }

    /// <summary>
    /// The restore, for the same reason <c>CancellationScope</c> has one: whatever runs after the
    /// filter must not read a deadline from a request that has finished.
    /// </summary>
    [Fact]
    public async Task TheDeadlineIsGoneWhenTheFilterReturns() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, new TimeoutFilter(LongBudget)).Next();

        Assert.Null(Accessor.Deadline);
        Assert.Equal(CancellationToken.None, Accessor.CancellationToken);
    }

    /// <summary>
    /// Nested budgets nest. An inner <c>[Timeout]</c> is what the handler inside it reads, and the
    /// outer one is what is visible again afterwards.
    /// </summary>
    [Fact]
    public async Task AnInnerBudgetIsWhatTheHandlerReadsAndTheOuterComesBack() {
        var context = Pipeline.Context();

        double inner = 0;
        double afterInner = 0;

        await Pipeline.Chain(
            context,
            new TimeoutFilter(LongBudget),
            new Pipeline.Inline(async chain => {
                await Pipeline.Chain(
                    chain.Context,
                    new TimeoutFilter(1_000),
                    new Pipeline.Inline(_ => {
                        inner = Accessor.Deadline!.Value.GetRemainingMilliseconds();

                        return Task.CompletedTask;
                    })).Next();

                afterInner = Accessor.Deadline!.Value.GetRemainingMilliseconds();
            })).Next();

        Assert.InRange(inner, 0, 1_000);
        Assert.InRange(afterInner, LongBudget - 5_000, LongBudget);
    }
}
