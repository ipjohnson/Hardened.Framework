using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Abstract.Timeouts;

/// <summary>
/// When the request being handled runs out of time.
///
/// <code>
/// public Task&lt;Quote&gt; Quote(Job job, IRequestDeadline deadline, CancellationToken cancellationToken) {
///     if (deadline.Deadline is { } at &amp;&amp; at.GetRemainingMilliseconds() &lt; 250) {
///         return Task.FromResult(Quote.Estimated(job));
///     }
///
///     return _pricing.Quote(job, cancellationToken);
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>The token says stop; this says how long there is.</b> A handler that only needs to abandon
/// its work when the budget runs out binds a <see cref="System.Threading.CancellationToken"/> and
/// passes it down. This is for the handler that has to decide something before starting: whether
/// there is time for a retry, for a second upstream call, or for the slow path rather than the
/// cached one.
/// </para>
/// <para>
/// <b>Registered as a singleton, so a handler of any lifetime can take it.</b> A described handler
/// implements a generated interface and cannot add a parameter to its own signature, so the
/// constructor is the only way in and a scoped registration would be captured by a singleton that
/// took one. The per-request value therefore lives in an <c>AsyncLocal</c> rather than on the
/// instance, which is the cost <c>[Timeout(Deadline = false)]</c> exists to decline.
/// </para>
/// <para>
/// <b>Null means nothing bounds this request.</b> Only <c>TimeoutFilter</c> publishes a deadline,
/// so a handler no budget applies to reads null here however it was called. Null is also what a
/// handler reads outside a request entirely.
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
}
