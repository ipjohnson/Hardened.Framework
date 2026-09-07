namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// A request that carries several items, each of which is handled on its own.
/// </summary>
/// <remarks>
/// <para>
/// A queue, a topic and a stream all deliver a batch against one source, so the route is chosen
/// once and the handler runs once per item. Splitting those two decisions is what keeps
/// <c>IPayloadAdapter.CreateRequest</c> returning a single request: an adapter that had to return
/// many would force every unbatched transport into a collection it has no use for.
/// </para>
/// <para>
/// <b>The mapping from an item to a request belongs to the transport, not to the filter.</b>
/// <see cref="ForItem"/> is implemented where the payload is understood - that a message attribute
/// is a header and a receipt handle is not is knowledge about SQS. The filter decides only when to
/// fork and what a failure means.
/// </para>
/// </remarks>
public interface IBatchRequest {
    /// <summary>How many items the delivery carried.</summary>
    int Count { get; }

    /// <summary>The request for one item, as a handler will see it.</summary>
    IExecutionRequest ForItem(int index);

    /// <summary>
    /// Whether this transport can report which individual items failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>False means a failed item must fail the whole invocation</b>, because there is no other
    /// way to stop the transport treating the item as handled. Swallowing it would lose the
    /// message.
    /// </para>
    /// <para>
    /// It is not a property of the source alone. SQS can report per-message failures only when the
    /// event source mapping was deployed with that turned on - a report sent to a mapping that did
    /// not ask for it is discarded and the whole batch is marked successful, which is message loss
    /// rather than a degraded report. So this defaults to false and is turned on by the deployment
    /// that turned it on at the other end. SNS has no equivalent at all.
    /// </para>
    /// </remarks>
    bool ReportsItemFailures { get; }

    /// <summary>
    /// What the rest of the batch means once one item has failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not defaulted. A transport that got this wrong would still pass every test and
    /// still report failures - it would just run some items twice on every retry, or leave some
    /// unattempted - so a new implementation is made to answer rather than allowed to inherit an
    /// answer that happens to suit a queue.
    /// </para>
    /// <para>
    /// Only consulted where <see cref="ReportsItemFailures"/> is true.
    /// </para>
    /// </remarks>
    BatchFailureMode FailureMode { get; }

    /// <summary>
    /// Records that an item was not handled, so the adapter can name it in the transport's report.
    /// </summary>
    /// <remarks>
    /// Only reached when <see cref="ReportsItemFailures"/> is true. The exception is passed so an
    /// implementation can log what happened against the item's own identity; nothing is required to
    /// keep it.
    /// </remarks>
    void RecordFailure(int index, Exception failure);

    /// <summary>The indexes recorded as failed, in the order they failed.</summary>
    IReadOnlyList<int> FailedItems { get; }
}
