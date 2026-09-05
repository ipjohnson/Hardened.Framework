using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Abstract.Timeouts;

/// <summary>
/// When the request being handled runs out of time.
///
/// <code>
/// public Task&lt;Quote&gt; Quote(Job job) {
///     if (_deadline.Deadline is { } at &amp;&amp; at.GetRemainingMilliseconds() &lt; 250) {
///         return Task.FromResult(Quote.Estimated(job));
///     }
///
///     return _pricing.Quote(job, _deadline.CancellationToken);
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>What the token cannot say is how long there is.</b> Cancellation tells a handler to stop; the
/// deadline is what lets it decide something before starting, such as whether there is time for a
/// retry, for a second upstream call, or for the slow path rather than the cached one. Both
/// readings come from the same publication, so they cannot describe different budgets.
/// </para>
/// <para>
/// <b>Registered as a singleton, so a handler of any lifetime can take it.</b> A described handler
/// implements a generated interface and cannot add a parameter to its own signature, so the
/// constructor is the only way in and a scoped registration would be captured by a singleton that
/// took one. The per-request value therefore lives in an <c>AsyncLocal</c> rather than on the
/// instance, which is the cost <c>[Timeout(Deadline = false)]</c> exists to decline.
/// </para>
/// <para>
/// <b>Nothing bounds every request.</b> Only <c>TimeoutFilter</c> publishes, so a handler no budget
/// applies to reads <see cref="Deadline"/> null and <see cref="CancellationToken"/>
/// <see cref="System.Threading.CancellationToken.None"/>, which is also what a handler reads
/// outside a request entirely. The two say the same thing and are never one without the other.
/// </para>
/// <para>
/// <b>This is not the way to get the request's token.</b> A handler that wants cancellation whether
/// or not a budget was declared - a client hanging up cancels either way - takes a
/// <see cref="System.Threading.CancellationToken"/> parameter, which a code-first handler writes on
/// its own signature and a described one gets from
/// <c>$(HardenedBindCancellationToken)</c>. The token here is the budget's, and it is
/// <see cref="System.Threading.CancellationToken.None"/> when there is no budget.
/// </para>
/// <para>
/// Monotonic, from <see cref="MachineTimestamp"/>, so a deadline is not moved by a clock
/// adjustment while a request is in flight. It is meaningful only on the machine that issued it.
/// </para>
/// </remarks>
public interface IRequestDeadline {

    /// <summary>
    /// When this request's budget runs out, or null when nothing bounds it.
    /// </summary>
    MachineTimestamp? Deadline { get; }

    /// <summary>
    /// The token that fires at <see cref="Deadline"/>, or
    /// <see cref="System.Threading.CancellationToken.None"/> when nothing bounds this request.
    /// </summary>
    /// <remarks>
    /// The same token the budget's own filter installed, so passing it to an upstream call is what
    /// makes the deadline reach that call. Beside the reading rather than left to the caller to
    /// find, because a handler that has just decided it has time to start something needs the token
    /// to start it with.
    /// </remarks>
    CancellationToken CancellationToken { get; }
}
