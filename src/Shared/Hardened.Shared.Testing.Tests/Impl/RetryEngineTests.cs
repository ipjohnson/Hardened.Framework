using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;
using HardenedTestContext = Hardened.Shared.Testing.Impl.TestContext;

namespace Hardened.Shared.Testing.Tests.Impl;

/// <summary>
/// <see cref="RetryEngine"/> is what a test polls an eventually-consistent system with. Its
/// contract is unusual in two ways that matter: an exception from the predicate is a retry rather
/// than a failure, and there is no attempt limit — the only way out other than success is
/// cancellation.
/// </summary>
/// <remarks>
/// Nothing here waits on the clock to decide whether it passed. Every stopping condition is driven
/// by the predicate itself — returning, throwing, or cancelling the token — so the result does not
/// change on a loaded machine. The engine's own retry interval is a hard-coded one second, so tests
/// that must go round the loop twice cost a second each; that is a floor, not a margin, and cannot
/// make them flake.
/// </remarks>
public class RetryEngineTests {

    private static (IRetryEngine Retry, RecordingLogger Logger) Engine(CancellationTokenSource? cancellation = null) {
        var logger = new RecordingLogger();
        var token = cancellation?.Token ?? CancellationToken.None;

        return (new HardenedTestContext(token, logger).Retry, logger);
    }

    // ---- TillTrue -----------------------------------------------------------------------------

    [Fact]
    public async Task TillTrueStopsOnTheFirstTruePredicate() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillTrue(() => {
            attempts++;
            return Task.FromResult(true);
        }, "waiting");

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task TillTrueKeepsAskingWhileThePredicateIsFalse() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillTrue(() => {
            attempts++;
            return Task.FromResult(attempts == 2);
        }, "waiting");

        Assert.Equal(2, attempts);
    }

    // ---- TillFalse ----------------------------------------------------------------------------

    [Fact]
    public async Task TillFalseStopsOnTheFirstFalsePredicate() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillFalse(() => {
            attempts++;
            return Task.FromResult(false);
        }, "waiting");

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task TillFalseKeepsAskingWhileThePredicateIsTrue() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillFalse(() => {
            attempts++;
            return Task.FromResult(attempts != 2);
        }, "waiting");

        Assert.Equal(2, attempts);
    }

    // ---- TillValue ----------------------------------------------------------------------------

    [Fact]
    public async Task TillValueHandsBackWhateverThePredicateProduced() {
        var (retry, _) = Engine();

        var result = await retry.TillValue(() => Task.FromResult("produced"), "waiting");

        Assert.Equal("produced", result);
    }

    [Fact]
    public async Task TillValueAsksOnlyOnceWhenTheFirstCallSucceeds() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillValue(() => {
            attempts++;
            return Task.FromResult(attempts);
        }, "waiting");

        Assert.Equal(1, attempts);
    }

    // ---- exceptions in the predicate ----------------------------------------------------------

    /// <summary>
    /// An exception from the predicate is treated as "not yet", not as a failure. Polling a service
    /// that is still starting throws before it answers, and a retry engine that gave up on the first
    /// throw would be useless for the case it exists to serve.
    /// </summary>
    [Fact]
    public async Task AThrownPredicateIsRetriedRatherThanFailingTheCaller() {
        var (retry, _) = Engine();
        var attempts = 0;

        await retry.TillTrue(() => {
            attempts++;
            if (attempts == 1) {
                throw new InvalidOperationException("not ready");
            }

            return Task.FromResult(true);
        }, "waiting");

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task AThrownPredicateIsLoggedSoTheReasonSurvives() {
        var (retry, logger) = Engine();
        var attempts = 0;

        await retry.TillTrue(() => {
            attempts++;
            if (attempts == 1) {
                throw new InvalidOperationException("not ready");
            }

            return Task.FromResult(true);
        }, "waiting");

        var failure = Assert.Single(logger.Entries, entry => entry.Exception != null);

        Assert.Equal("not ready", failure.Exception!.Message);
    }

    [Fact]
    public async Task TillValueRetriesAThrownPredicateAndReturnsTheLaterValue() {
        var (retry, _) = Engine();
        var attempts = 0;

        var result = await retry.TillValue(() => {
            attempts++;
            if (attempts == 1) {
                throw new InvalidOperationException("not ready");
            }

            return Task.FromResult("eventually");
        }, "waiting");

        Assert.Equal("eventually", result);
    }

    // ---- cancellation, which is the only timeout there is --------------------------------------

    /// <summary>
    /// A token already cancelled stops the engine before it asks anything, so a cancelled run does
    /// not get one more round trip against a system that is going away.
    /// </summary>
    [Fact]
    public async Task ATokenAlreadyCancelledStopsBeforeTheFirstAttempt() {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var (retry, _) = Engine(source);
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.TillTrue(() => {
            attempts++;
            return Task.FromResult(false);
        }, "waiting"));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task CancellingWhileWaitingEndsTheRetryLoop() {
        using var source = new CancellationTokenSource();

        var (retry, _) = Engine(source);
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.TillTrue(() => {
            attempts++;
            source.Cancel();
            return Task.FromResult(false);
        }, "waiting"));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CancellingEndsTillFalseToo() {
        using var source = new CancellationTokenSource();

        var (retry, _) = Engine(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.TillFalse(() => {
            source.Cancel();
            return Task.FromResult(true);
        }, "waiting"));
    }

    [Fact]
    public async Task CancellingEndsTillValueToo() {
        using var source = new CancellationTokenSource();

        var (retry, _) = Engine(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.TillValue<string>(() => {
            source.Cancel();
            throw new InvalidOperationException("not ready");
        }, "waiting"));
    }

    // ---- logging ------------------------------------------------------------------------------

    [Fact]
    public async Task EveryAttemptIsLoggedWithTheCallersDescription() {
        var (retry, logger) = Engine();
        var attempts = 0;

        await retry.TillTrue(() => {
            attempts++;
            return Task.FromResult(attempts == 2);
        }, "waiting for {resource}", "queue");

        var described = logger.Entries.Where(entry => entry.Value("resource") is "queue").ToArray();

        Assert.Equal(2, described.Length);
        Assert.All(described, entry => Assert.Equal(LogLevel.Information, entry.Level));
    }

    // ---- the delay ----------------------------------------------------------------------------

    /// <summary>
    /// <see cref="IRetryEngine.Delay"/> documents a default of one second, and that default is what
    /// the engine waits.
    /// </summary>
    /// <remarks>
    /// Only the default is asserted. As of 2026-08-11 <see cref="RetryEngine"/> never reads the
    /// property — all three loops call <c>Task.Delay(1000, …)</c> with a literal — so setting it has
    /// no effect. That is reported as a defect rather than pinned here: a test asserting that a
    /// changed Delay is ignored would have to be deleted by whoever fixes it.
    /// </remarks>
    [Fact]
    public void TheRetryIntervalDefaultsToOneSecond() {
        var (retry, _) = Engine();

        Assert.Equal(1000, retry.Delay);
    }
}
