using System.Runtime.ExceptionServices;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Runs the rest of the chain once per item when the request is a batch.
/// </summary>
/// <remarks>
/// <para>
/// One delivery, many handler invocations. The route was decided once, from the queue or topic the
/// batch arrived against, and everything after this filter - binding, validation, the handler -
/// happens per item because each item has its own body and its own headers.
/// </para>
/// <para>
/// <b>A failure is a value, not a throw.</b> The invoke filters catch whatever a handler raised and
/// record it on <see cref="IExecutionResponse.ExceptionValue"/> rather than letting it propagate,
/// so a filter that only caught exceptions would see every failed item as handled and report an
/// empty failure list. The response is read after each item for that reason; the <c>catch</c> is
/// for the filters that do still throw. <c>RetryFilter</c> learned this the same way.
/// </para>
/// <para>
/// <b>Every item is attempted, even after one fails</b>, when the transport can report failures
/// individually. Stopping at the first would leave the rest unhandled and unreported, so the
/// transport would treat them as delivered.
/// </para>
/// <para>
/// <b>When the transport cannot report individual failures, the first failure is rethrown.</b> That
/// fails the invocation, which is the only thing that makes the transport redeliver. Continuing and
/// swallowing would lose messages; see <see cref="IBatchRequest.ReportsItemFailures"/> for why that
/// is the default even on a transport that supports reporting.
/// </para>
/// </remarks>
public class BatchExecutionFilter : IExecutionFilter {
    public async Task Execute(IExecutionChain chain) {
        // Not a batch, or an empty one. An empty delivery is not an error - nothing was asked for
        // and nothing is reported - and running the chain once keeps an unbatched transport
        // completely unaffected by this filter being registered globally.
        if (chain.Context.Request is not IBatchRequest batch || batch.Count == 0) {
            await chain.Next();

            return;
        }

        for (var index = 0; index < batch.Count; index++) {
            var failure = await Item(chain, batch, index);

            if (failure == null) {
                continue;
            }

            if (!batch.ReportsItemFailures) {
                // Rethrown with its stack rather than raised anew, so what reaches the host names
                // where the handler failed rather than this loop.
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            batch.RecordFailure(index, failure);
        }
    }

    /// <summary>
    /// One item on its own fork, and whatever it failed with.
    /// </summary>
    /// <remarks>
    /// Its own response as well as its own request, so one item's status or exception cannot be
    /// read as another's. <see cref="IExecutionChain.Fork"/> copies the chain at this position,
    /// which is what makes "run the rest of this again" expressible.
    /// </remarks>
    private static async Task<Exception?> Item(IExecutionChain chain, IBatchRequest batch, int index) {
        var context = chain.Context.Clone(
            request: batch.ForItem(index),
            response: chain.Context.Response.Clone(null));

        try {
            await chain.Fork(context).Next();

            return context.Response.ExceptionValue;
        }
        catch (Exception exception) {
            return exception;
        }
    }
}
