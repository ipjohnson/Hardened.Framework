using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// <c>[Timeout]</c> through the real pipeline, where a deadline is the handler's own
/// <c>CancellationToken</c> parameter being cancelled.
/// </summary>
/// <remarks>
/// The bind is the part only a built application answers. The token a handler declares is copied
/// out of the execution context as the request is bound, at
/// <c>FilterOrder.Serialization</c> - so a filter placed one stage later would hand every handler
/// here the transport's token and every one of these tests would still pass against a feature
/// that does nothing. The status is the other part: it is decided by
/// <c>ExceptionToModelConverter</c> during serialization, which happens inside the filter's span
/// and cannot be observed by driving the filter alone.
/// </remarks>
[BasePath("/timeout")]
public class TimeoutController {

    /// <summary>
    /// Short enough that a test does not wait on it, long enough not to race a slow CI box.
    /// </summary>
    /// <remarks>
    /// Public because the generator re-emits an attribute's arguments into the handler's metadata
    /// array verbatim, so a private constant named on an attribute does not compile there.
    /// </remarks>
    public const int ShortBudget = 100;

    private readonly HandlerCallCounter _counter;

    public TimeoutController(HandlerCallCounter counter) {
        _counter = counter;
    }

    /// <summary>
    /// Waits for something that never comes, on the token the deadline replaced. 504.
    /// </summary>
    [Get("/slow")]
    [Timeout(Milliseconds = ShortBudget)]
    public async Task<string> Slow(CancellationToken cancellationToken) {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        return "never";
    }

    /// <summary>
    /// The same handler with room to finish, so the deadline is not what decides its answer.
    /// </summary>
    [Get("/fast")]
    [Timeout(Milliseconds = 60_000)]
    public string Fast() => "fast-" + _counter.Next("fast");

    /// <summary>
    /// An operation shedding load rather than waiting on a dependency, which is the one case that
    /// knows its own window.
    /// </summary>
    [Get("/shed")]
    [Timeout(Milliseconds = ShortBudget, Status = 503, RetryAfterSeconds = 30)]
    public async Task<string> Shed(CancellationToken cancellationToken) {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        return "never";
    }

    /// <summary>
    /// Declares nothing of its own, so the cascade falls through to this assembly's
    /// <c>[assembly: Timeout]</c>.
    /// </summary>
    [Get("/unbounded")]
    public string Unbounded() => "unbounded-" + _counter.Next("unbounded");

    /// <summary>
    /// What the cascade actually resolved for this operation, read back off the handler.
    /// </summary>
    /// <remarks>
    /// The point of making the budget first-class: the filter that enforces it and everything else
    /// that reads the handler see one value. A handler can therefore report its own deadline, which
    /// is how a rung nothing on the handler declares - its assembly, the entry point's default, a
    /// convention - is assertable at all without waiting for it to fire.
    /// </remarks>
    [Get("/budget")]
    public int Budget(IExecutionContext context) =>
        context.HandlerInfo?.Timeout?.Milliseconds ?? -1;

    /// <summary>
    /// One budget across every attempt rather than one per attempt, which is what putting the
    /// filter ahead of <c>FilterOrder.Retry</c> buys.
    /// </summary>
    /// <remarks>
    /// Five attempts at 40ms of backoff each cannot fit in 150ms, so the budget is what stops this
    /// rather than the attempt count. A per-attempt deadline would let all five run.
    /// </remarks>
    [Get("/retried")]
    [Timeout(Milliseconds = 150)]
    [Retry(Attempts = 5, SleepTime = 40, TotalBudget = 0)]
    public string Retried() =>
        throw new InvalidOperationException("attempt " + _counter.Next("retried"));

    /// <summary>How many times a handler has run, so an attempt count is assertable.</summary>
    [Get("/calls/{handler}")]
    public int Calls(string handler) => _counter.Count(handler);
}

/// <summary>
/// A class-level deadline, and a method that overrides it in the direction only a nearest-wins rule
/// allows.
/// </summary>
/// <remarks>
/// The class beats the assembly's five minutes; the method beats the class, upwards. A
/// tightest-wins rule would hand <see cref="Slower"/> the class's twenty seconds and nothing would
/// say it had.
/// </remarks>
[BasePath("/timeout/classed")]
[Timeout(Milliseconds = 20_000)]
public class ClassTimeoutController {

    /// <summary>Inherits its class, which beat the assembly.</summary>
    [Get("/budget")]
    public int Budget(IExecutionContext context) =>
        context.HandlerInfo?.Timeout?.Milliseconds ?? -1;

    /// <summary>Loosens its class, which only the nearest declaration may do.</summary>
    [Get("/slower")]
    [Timeout(Milliseconds = 40_000)]
    public int Slower(IExecutionContext context) =>
        context.HandlerInfo?.Timeout?.Milliseconds ?? -1;
}
