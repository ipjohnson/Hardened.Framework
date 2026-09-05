using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Timeouts;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Bounds how long the rest of the chain may take, by replacing the token everything behind it
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not answer the timeout.</b> <c>IOFilter</c> sits at
/// <see cref="Hardened.Requests.Abstract.RequestFilter.FilterOrder.Serialization"/>, which is
/// inside this span: by the time this filter regains control the response has already been
/// serialized from whatever the handler raised. The status therefore comes from
/// <c>ExceptionToModelConverter</c>, which maps an <see cref="OperationCanceledException"/> to 504,
/// and this filter's whole job on the way out is to restore the token and dispose the source.
/// </para>
/// <para>
/// <b>Cancellation is cooperative, so a handler that ignores the token is not bounded.</b> The
/// deadline reaches a handler that declares a <c>CancellationToken</c> parameter, and anything
/// passing <c>IExecutionContext.CancellationToken</c> to the work it awaits. A handler that blocks
/// a thread runs to completion and the request answers late; nothing here can take a thread back.
/// </para>
/// <para>
/// <b>It also publishes the deadline, for the handler that has to decide before it starts.</b>
/// The token says stop and says nothing about how long there was;
/// <see cref="IRequestDeadline"/> answers the second question. Publishing it writes an
/// <c>AsyncLocal</c>, which every continuation after it copies, so
/// <c>[Timeout(Deadline = false)]</c> turns it off for a handler that will not read it.
/// </para>
/// <para>
/// See <see cref="CancellationScope"/> for why the token is put back rather than left swapped, and
/// <see cref="DeadlineScope"/> for the deadline's own restore.
/// </para>
/// </remarks>
public class TimeoutFilter : IExecutionFilter {
    private readonly int _milliseconds;
    private readonly bool _publishDeadline;

    public TimeoutFilter(int milliseconds, bool publishDeadline = true) {
        _milliseconds = milliseconds;
        _publishDeadline = publishDeadline;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // Linked, so the transport's own cancellation still reaches the handler: a client that
        // hangs up should stop the work whether or not a budget was declared.
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        deadline.CancelAfter(_milliseconds);

        try {
            using (context.WithCancellation(deadline.Token))
            using (RequestDeadline.Until(Published())) {
                await chain.Next();
            }
        }
        finally {
            // The scope has closed by the time this runs, so the token read here is the
            // transport's again. Both fire together on a disconnect, and that is the case this
            // excludes: the metric is here to find the slow handler, not to count clients closing
            // tabs.
            //
            // In a finally because a filter that throws past this one would otherwise take the
            // count with it. Nothing in the shipping pipeline does - IOFilter catches at
            // Serialization, inside this span - but a chain that has been composed by hand can.
            if (deadline.IsCancellationRequested &&
                !context.CancellationToken.IsCancellationRequested) {
                context.RequestMetrics.Record(RequestMetrics.RequestTimedOut, 1);
            }
        }
    }

    /// <summary>The deadline to publish, or null when this handler declined it.</summary>
    /// <remarks>
    /// Taken now rather than from the request's start, because now is when the budget starts
    /// running: <c>CancelAfter</c> is called on the same pass, and a deadline measured from
    /// anywhere else would disagree with the token enforcing it.
    /// </remarks>
    private MachineTimestamp? Published() =>
        _publishDeadline ? MachineTimestamp.Now.AddMs(_milliseconds) : null;
}
