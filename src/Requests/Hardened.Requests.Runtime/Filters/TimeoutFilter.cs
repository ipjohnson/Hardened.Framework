using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;

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
/// See <see cref="CancellationScope"/> for why the token is put back rather than left swapped.
/// </para>
/// </remarks>
public class TimeoutFilter : IExecutionFilter {
    private readonly int _milliseconds;

    public TimeoutFilter(int milliseconds) {
        _milliseconds = milliseconds;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // Linked, so the transport's own cancellation still reaches the handler: a client that
        // hangs up should stop the work whether or not a budget was declared.
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        deadline.CancelAfter(_milliseconds);

        try {
            using (context.WithCancellation(deadline.Token)) {
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
}
