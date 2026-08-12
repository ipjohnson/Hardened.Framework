using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;
using HardenedTestContext = Hardened.Shared.Testing.Impl.TestContext;

namespace Hardened.Shared.Testing.Tests.Impl;

/// <summary>
/// <c>ITestContext.Step</c> is how a Hardened test narrates itself: each step reports whether it
/// passed and how long it took, so a failure in CI names the step rather than a stack frame.
/// </summary>
/// <remarks>
/// <para>
/// The outcome and the duration are logged as named values in the message template, not as prose,
/// so these assert on the structured state. Asserting on the rendered string would pass just as
/// happily for a step logged as passing with a duration of "fail".
/// </para>
/// <para>
/// Steps are handed over as explicitly typed delegates rather than as lambdas written inline. The
/// four overloads differ only in the delegate they take, and several of them accept the same
/// lambda — so an inline lambda would let overload resolution decide which overload each test
/// covers, which is the one thing these tests are not allowed to leave to chance.
/// </para>
/// </remarks>
public class TestContextStepTests {

    private static (ITestContext Context, RecordingLogger Logger) Context() {
        var logger = new RecordingLogger();

        return (new HardenedTestContext(CancellationToken.None, logger), logger);
    }

    private sealed class StepFailed : Exception {
        public StepFailed() : base("step failed") { }
    }

    /// <summary>
    /// Binds the <c>Task&lt;T&gt; Step&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, …)</c> overload by
    /// signature, which naming it in a call cannot do — <c>Func&lt;Task&lt;T&gt;&gt;</c> is equally
    /// good an argument for the <c>Func&lt;T&gt;</c> overload with T bound to the task itself.
    /// </summary>
    private static Task<T> AsyncFuncStep<T>(ITestContext context, Func<Task<T>> step, string description,
        params object[] parameters) {
        Func<Func<Task<T>>, string, object[], Task<T>> overload = context.Step<T>;

        return overload(step, description, parameters);
    }

    // ---- the four overloads, passing ----------------------------------------------------------

    [Fact]
    public void AnActionStepThatReturnsIsLoggedAsPassing() {
        var (context, logger) = Context();
        Action step = () => { };

        context.Step(step, "doing the thing");

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("pass", entry.Value("status"));
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    [Fact]
    public void AFuncStepHandsBackItsResultAndIsLoggedAsPassing() {
        var (context, logger) = Context();
        Func<int> step = () => 42;

        var result = context.Step(step, "computing the thing");

        Assert.Equal(42, result);
        Assert.Equal("pass", Assert.Single(logger.Entries).Value("status"));
    }

    [Fact]
    public async Task AnAsyncStepThatCompletesIsLoggedAsPassing() {
        var (context, logger) = Context();
        Func<Task> step = () => Task.CompletedTask;

        await context.Step(step, "awaiting the thing");

        Assert.Equal("pass", Assert.Single(logger.Entries).Value("status"));
    }

    [Fact]
    public async Task AnAsyncFuncStepHandsBackItsResultAndIsLoggedAsPassing() {
        var (context, logger) = Context();

        var result = await AsyncFuncStep(context, () => Task.FromResult("value"), "awaiting the thing");

        Assert.Equal("value", result);
        Assert.Equal("pass", Assert.Single(logger.Entries).Value("status"));
    }

    // ---- the four overloads, failing -----------------------------------------------------------

    /// <summary>
    /// A failing step is logged at Error, and the exception still reaches the caller — the log
    /// narrates the failure, it does not absorb it.
    /// </summary>
    [Fact]
    public void AFailingActionStepIsLoggedAsFailingAndStillThrows() {
        var (context, logger) = Context();
        Action step = () => throw new StepFailed();

        Assert.Throws<StepFailed>(() => context.Step(step, "doing the thing"));

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("fail", entry.Value("status"));
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public void AFailingFuncStepIsLoggedAsFailingAndStillThrows() {
        var (context, logger) = Context();
        Func<int> step = () => throw new StepFailed();

        Assert.Throws<StepFailed>(() => context.Step(step, "computing"));

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("fail", entry.Value("status"));
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task AFailingAsyncStepIsLoggedAsFailingAndStillThrows() {
        var (context, logger) = Context();
        Func<Task> step = () => Task.FromException(new StepFailed());

        await Assert.ThrowsAsync<StepFailed>(() => context.Step(step, "awaiting"));

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("fail", entry.Value("status"));
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task AFailingAsyncFuncStepIsLoggedAsFailingAndStillThrows() {
        var (context, logger) = Context();

        await Assert.ThrowsAsync<StepFailed>(
            () => AsyncFuncStep(context, () => Task.FromException<int>(new StepFailed()), "awaiting"));

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("fail", entry.Value("status"));
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    // ---- what the log carries -----------------------------------------------------------------

    [Fact]
    public void TheCallersDescriptionAndItsParametersReachTheLog() {
        var (context, logger) = Context();
        Action step = () => { };

        context.Step(step, "fetching {resource} for {user}", "orders", "ian");

        var entry = Assert.Single(logger.Entries);

        Assert.Equal("orders", entry.Value("resource"));
        Assert.Equal("ian", entry.Value("user"));
        Assert.Contains("orders", entry.Message);
    }

    [Fact]
    public void TheOutcomeLeadsTheMessageAndTheDurationEndsIt() {
        var (context, logger) = Context();
        Action step = () => { };

        context.Step(step, "doing the thing");

        var message = Assert.Single(logger.Entries).Message;

        Assert.StartsWith("pass - doing the thing - ", message);
        Assert.EndsWith("ms", message);
    }

    /// <summary>
    /// The duration is measured around the step, so a step that takes time reports it. Asserted
    /// against a floor an order of magnitude below what the step actually sleeps, because a loaded
    /// machine can only make the measurement larger.
    /// </summary>
    [Fact]
    public void ThePassingDurationCoversTheWorkTheStepDid() {
        var (context, logger) = Context();
        Action step = () => Thread.Sleep(200);

        context.Step(step, "sleeping");

        var duration = Assert.IsType<double>(Assert.Single(logger.Entries).Value("duration"));

        Assert.True(duration >= 20, $"expected the logged duration to cover the step, was {duration}ms");
    }

    [Fact]
    public void AFailingStepStillReportsHowLongItRanBeforeItFailed() {
        var (context, logger) = Context();
        Action step = () => {
            Thread.Sleep(200);
            throw new StepFailed();
        };

        Assert.Throws<StepFailed>(() => context.Step(step, "sleeping then failing"));

        var duration = Assert.IsType<double>(Assert.Single(logger.Entries).Value("duration"));

        Assert.True(duration >= 20, $"expected the logged duration to cover the step, was {duration}ms");
    }

    // ---- nesting ------------------------------------------------------------------------------

    /// <summary>
    /// Steps nest, and the inner one is reported first — it finishes first. A reader following the
    /// log sees the detail before the summary that contains it.
    /// </summary>
    [Fact]
    public void ANestedStepIsReportedBeforeTheStepThatContainsIt() {
        var (context, logger) = Context();
        Action inner = () => { };
        Action outer = () => context.Step(inner, "inner");

        context.Step(outer, "outer");

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("inner", logger.Entries[0].Message);
        Assert.Contains("outer", logger.Entries[1].Message);
    }

    [Fact]
    public void AContainingStepsDurationIncludesTheStepsInsideIt() {
        var (context, logger) = Context();
        Action inner = () => Thread.Sleep(100);
        Action outer = () => context.Step(inner, "inner");

        context.Step(outer, "outer");

        var innerDuration = Assert.IsType<double>(logger.Entries[0].Value("duration"));
        var outerDuration = Assert.IsType<double>(logger.Entries[1].Value("duration"));

        Assert.True(outerDuration >= innerDuration,
            $"outer step reported {outerDuration}ms, which is less than the inner {innerDuration}ms");
    }

    /// <summary>
    /// A failure inside a nested step is not absorbed by the step containing it: the exception
    /// propagates, so both are reported as failures rather than one passing over the other.
    /// </summary>
    [Fact]
    public void AFailureInsideANestedStepFailsBothSteps() {
        var (context, logger) = Context();
        Action inner = () => throw new StepFailed();
        Action outer = () => context.Step(inner, "inner");

        Assert.Throws<StepFailed>(() => context.Step(outer, "outer"));

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal("fail", entry.Value("status")));
    }

    [Fact]
    public async Task AsyncStepsNestTheSameWayAsSynchronousOnes() {
        var (context, logger) = Context();

        var result = await AsyncFuncStep(context,
            () => AsyncFuncStep(context, () => Task.FromResult(7), "inner"),
            "outer");

        Assert.Equal(7, result);
        Assert.Contains("inner", logger.Entries[0].Message);
        Assert.Contains("outer", logger.Entries[1].Message);
    }
}
