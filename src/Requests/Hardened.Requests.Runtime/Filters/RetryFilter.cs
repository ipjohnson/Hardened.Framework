using System.Runtime.ExceptionServices;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Runs the rest of the chain again when an attempt fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failure is a value, not a throw.</b> <c>InvokeNoParametersFilter</c> and its siblings catch
/// whatever the handler raised and hand it to <see cref="ControllerErrorHelper"/>, which records it
/// on <see cref="IExecutionResponse.ExceptionValue"/> and returns a completed task. Nothing
/// propagates. A retry filter that only caught exceptions therefore saw every failed attempt as a
/// success and stopped after one - which is exactly what the previous implementation did. The
/// response is read after each attempt for that reason; the <c>catch</c> is for the filters that do
/// still throw.
/// </para>
/// <para>
/// <b>Each attempt runs on a fork.</b> <see cref="IExecutionChain.Next"/> advances an index the
/// chain owns, so calling it a second time runs whatever is left rather than the same work again -
/// and after a full pass there is nothing left, so it returns immediately.
/// <see cref="IExecutionChain.Fork"/> copies the chain at its current position, which is what makes
/// "run the rest of this again" expressible at all.
/// </para>
/// <para>
/// <b>Not everything should be retried.</b> The default predicate declines client errors: a 400 does
/// not become a 200 by being asked three times, and retrying one costs the caller the whole backoff
/// budget to be told what the first attempt already knew. Non-idempotent verbs are declined for a
/// blunter reason - a retried <c>POST</c> is a second write.
/// </para>
/// </remarks>
public class RetryFilter : IExecutionFilter {
    private readonly int _attempts;
    private readonly int _baseDelayMilliseconds;
    private readonly int _totalBudgetMilliseconds;
    private readonly bool _allowNonIdempotent;
    private readonly Func<Exception, bool> _shouldRetry;

    /// <summary>
    /// The verbs a retry is safe on without the handler author saying so.
    /// </summary>
    /// <remarks>
    /// RFC 9110 §9.2.2. <c>POST</c> and <c>PATCH</c> are absent because replaying one is a second
    /// side effect, and the framework has no way to know whether that is acceptable.
    /// </remarks>
    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "PUT", "DELETE", "OPTIONS", "TRACE" };

    public RetryFilter(
        int attempts,
        int baseDelayMilliseconds,
        int totalBudgetMilliseconds,
        bool allowNonIdempotent,
        Func<Exception, bool>? shouldRetry = null) {
        _attempts = attempts;
        _baseDelayMilliseconds = baseDelayMilliseconds;
        _totalBudgetMilliseconds = totalBudgetMilliseconds;
        _allowNonIdempotent = allowNonIdempotent;
        _shouldRetry = shouldRetry ?? IsTransient;
    }

    /// <summary>
    /// Whether a failure is worth another attempt.
    /// </summary>
    /// <remarks>
    /// Declines anything the caller caused. <see cref="IStatusCodeException"/> is checked by status
    /// rather than by type so that an application's own exception naming a 4xx is declined too,
    /// without having to derive from anything in particular.
    /// </remarks>
    public static bool IsTransient(Exception exception) =>
        exception switch {
            OperationCanceledException => false,
            BadRequestException => false,
            FormatException => false,
            IStatusCodeException { StatusCode: >= 400 and < 500 } => false,
            _ => true
        };

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        if (_attempts <= 1 || !MayRetry(context)) {
            await chain.Next();

            return;
        }

        var response = context.Response;

        // What the response looked like before any attempt ran, so a failed attempt's partial
        // answer is not still sitting there when the next one starts.
        var status = response.Status;
        var responseValue = response.ResponseValue;

        var start = MachineTimestamp.Now;

        for (var attempt = 1;; attempt++) {
            if (attempt > 1) {
                response.Status = status;
                response.ResponseValue = responseValue;

                // A handler reading the body directly rather than through bound parameters needs it
                // back at the start. Parameters themselves are already bound and cached by the time
                // this filter runs, so nothing re-reads the stream on their behalf.
                if (context.Request.Body.CanSeek) {
                    context.Request.Body.Position = 0;
                }
            }

            response.ExceptionValue = null;

            Exception? failure;

            try {
                await chain.Fork(context).Next();

                failure = response.ExceptionValue;
            }
            catch (Exception exception) {
                failure = exception;
            }

            if (failure == null) {
                return;
            }

            if (attempt >= _attempts ||
                !_shouldRetry(failure) ||
                context.CancellationToken.IsCancellationRequested ||
                Exhausted(start)) {
                // Back onto the response rather than rethrown: the filter at
                // FilterOrder.Serialization is what turns a failure into a body, and it reads it
                // from here. Rethrowing would skip it and hand the exception to the transport.
                response.ExceptionValue = failure;

                return;
            }

            await Delay(attempt, context.CancellationToken);
        }
    }

    /// <summary>
    /// Whether this request is one a retry may be attempted on at all.
    /// </summary>
    private bool MayRetry(IExecutionContext context) =>
        _allowNonIdempotent || IdempotentMethods.Contains(context.Request.Method);

    private bool Exhausted(MachineTimestamp start) =>
        _totalBudgetMilliseconds > 0 && start.GetElapsedMilliseconds() >= _totalBudgetMilliseconds;

    /// <summary>
    /// Exponential backoff with full jitter, bounded by whatever is left of the budget.
    /// </summary>
    /// <remarks>
    /// Jittered because the failure being retried is usually shared: every request that hit the same
    /// slow dependency backs off by the same fixed amount and returns together, which is the
    /// dependency's second outage. A delay drawn from <c>[0, window)</c> spreads them instead.
    /// </remarks>
    private Task Delay(int attempt, CancellationToken cancellationToken) {
        if (_baseDelayMilliseconds <= 0) {
            return Task.CompletedTask;
        }

        // Shifting rather than Math.Pow, and capped, so a large attempt count cannot overflow into
        // a negative window.
        var exponent = Math.Min(attempt - 1, 20);
        var window = (long)_baseDelayMilliseconds << exponent;

        if (_totalBudgetMilliseconds > 0) {
            window = Math.Min(window, _totalBudgetMilliseconds);
        }

        return Task.Delay(Random.Shared.Next((int)Math.Min(window, int.MaxValue) + 1), cancellationToken);
    }
}
