using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Kinesis;

/// <summary>
/// One Kinesis delivery: a stream, and the records a shard carried.
/// </summary>
/// <remarks>
/// One request for the batch, forked per record, the same as a queue delivery - an event source
/// mapping is attached to one stream, so the route is decided once. Where it stops resembling a
/// queue is the failure: a shard is an ordered log and Lambda re-drives it by position, so the
/// report names where to rewind to rather than which records to redeliver.
/// </remarks>
public class KinesisRequest : LambdaPayloadRequest, IBatchRequest {
    private readonly List<int> _failed = [];

    public KinesisRequest(
        string streamName,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<KinesisRecord> records,
        bool reportsItemFailures = false)
        : base(StreamScheme, "/" + streamName, body, headers) {
        Records = records;
        ReportsItemFailures = reportsItemFailures;
    }

    /// <summary>The scheme a stream routes under, which <c>[Stream]</c> declares.</summary>
    public const string StreamScheme = "STREAM";

    /// <summary>What the publisher chose to order by.</summary>
    public const string PartitionKeyHeader = "x-amz-kinesis-partition-key";

    /// <summary>The record's position in the shard, which is what a failure report names.</summary>
    public const string SequenceNumberHeader = "x-amz-kinesis-sequence-number";

    /// <summary>Shard and sequence number together, which is the only place the shard appears.</summary>
    public const string EventIdHeader = "x-amz-kinesis-event-id";

    /// <summary>When Kinesis accepted the record, as epoch seconds.</summary>
    public const string ArrivalTimeHeader = "x-amz-kinesis-arrival-time";

    /// <summary>The stream, in full.</summary>
    public const string StreamArnHeader = "x-amz-kinesis-stream-arn";

    /// <summary>The records, in the order the shard delivered them.</summary>
    /// <remarks>
    /// Order is the point here in a way it is not on a standard queue. A shard promises the records
    /// written under one partition key in the order they were written, so a filter that reordered a
    /// batch would break the guarantee the stream exists to give.
    /// </remarks>
    public IReadOnlyList<KinesisRecord> Records { get; }

    public int Count => Records.Count;

    public IExecutionRequest ForItem(int index) => ForRecord(Records[index]);

    /// <summary>
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>.
    /// </summary>
    /// <remarks>
    /// Off unless the deployment says otherwise, for the reason it is off for SQS: a report sent to
    /// a mapping that did not ask for one is discarded and the batch marked wholly successful.
    /// </remarks>
    public bool ReportsItemFailures { get; }

    /// <summary>
    /// By checkpoint, because a shard is a log and the report is a position in it.
    /// </summary>
    /// <remarks>
    /// Lambda takes the lowest sequence number reported and replays the shard from there, so every
    /// record after a failure is coming back whether or not it ran. Running them first is a
    /// duplicate delivery, and it applies a later record before the replay of an earlier one -
    /// which is the ordering a shard exists to give.
    /// </remarks>
    public BatchFailureMode FailureMode => BatchFailureMode.Checkpoint;

    public IReadOnlyList<int> FailedItems => _failed;

    public void RecordFailure(int index, Exception failure) => _failed.Add(index);

    /// <summary>
    /// The sequence numbers the report has to name.
    /// </summary>
    /// <remarks>
    /// The sequence number, never the event id. The event id carries a shard prefix and is not a
    /// position Lambda can resolve a checkpoint from - the same mistake Hardened.Amz shipped on the
    /// DynamoDB side until 2026-08-15, where it silently re-drove the whole batch.
    /// </remarks>
    public IEnumerable<string> FailedSequenceNumbers =>
        _failed.Select(index => Records[index].SequenceNumber)
            .Where(number => !string.IsNullOrEmpty(number));

    /// <summary>
    /// The request for one record, as a handler will see it.
    /// </summary>
    /// <remarks>
    /// The body is the decoded data and nothing else. Kinesis wraps a blob it knows nothing about,
    /// so unlike a change feed there is no image to flatten - the handler binds the publisher's own
    /// bytes against its own parameter type, which is what the direct-invoke adapter does with a
    /// caller's payload.
    /// </remarks>
    public IExecutionRequest ForRecord(KinesisRecord record) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Set(headers, PartitionKeyHeader, record.PartitionKey);
        Set(headers, SequenceNumberHeader, record.SequenceNumber);
        Set(headers, EventIdHeader, record.EventId);
        Set(headers, ArrivalTimeHeader, record.ArrivalTime);

        if (Headers.TryGetValue(StreamArnHeader, out var arn)) {
            headers[StreamArnHeader] = arn;
        }

        return new LambdaPayloadRequest(
            Method, Path, new MemoryStream(record.Data.ToArray(), writable: false), headers);
    }

    private static void Set(
        IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
