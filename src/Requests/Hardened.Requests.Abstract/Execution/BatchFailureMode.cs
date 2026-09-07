namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// What a transport does with the items after one in a batch fails.
/// </summary>
/// <remarks>
/// <para>
/// Two transports can accept the identical failure report and mean different things by it, so
/// reporting is not one behaviour. SQS and Kinesis are both answered with a
/// <c>batchItemFailures</c> array of identifiers; SQS deletes everything not named, and Kinesis
/// rewinds to the earliest name and redelivers from there. Running the same loop against both is
/// correct for one of them.
/// </para>
/// <para>
/// Only consulted where <see cref="IBatchRequest.ReportsItemFailures"/> is true. Without reporting
/// the first failure fails the invocation whatever the mode, because that is the only thing that
/// makes any transport redeliver.
/// </para>
/// </remarks>
public enum BatchFailureMode {
    /// <summary>
    /// Each item is answered for on its own, so every item is attempted.
    /// </summary>
    /// <remarks>
    /// A queue. Items are independent, the report names exactly what to redeliver, and stopping
    /// early would leave the rest unattempted and unreported - which the transport reads as
    /// handled.
    /// </remarks>
    PerItem,

    /// <summary>
    /// The report is a position in an ordered log, so the run stops at the first failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shard. Naming an item tells the transport to redeliver the batch from that item onward,
    /// so everything after a failure is coming back regardless. Attempting those items first runs
    /// them twice, which a handler that is not idempotent experiences as a duplicate write rather
    /// than as a retry.
    /// </para>
    /// <para>
    /// Stopping is also what preserves the ordering the shard exists to give. A stream promises
    /// items in order per partition key, and handling item five after item three failed breaks that
    /// promise on the retry, where three is replayed after five has already been applied.
    /// </para>
    /// </remarks>
    Checkpoint
}
