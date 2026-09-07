using System.Text.Json;
using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// One DynamoDB Streams delivery: a table, and the changes to its rows.
/// </summary>
/// <remarks>
/// <para>
/// One request for the batch, forked per record, the same as a queue delivery - an event source
/// mapping is attached to one table's stream, so the route is decided once.
/// </para>
/// <para>
/// <b>Where it stops resembling a queue is the failure.</b> A shard is an ordered log and Lambda
/// re-drives it by position, so the report names where to rewind to rather than which items to
/// redeliver. See <see cref="FailureMode"/>.
/// </para>
/// </remarks>
public class DynamoDbRequest : LambdaPayloadRequest, IBatchRequest {
    private readonly List<int> _failed = [];

    public DynamoDbRequest(
        string tableName,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<DynamoDBEvent.DynamodbStreamRecord> records,
        bool reportsItemFailures = false)
        : base(ChangeScheme, "/" + tableName, body, headers) {
        Records = records;
        ReportsItemFailures = reportsItemFailures;
    }

    /// <summary>The scheme a change feed routes under, which <c>[Change]</c> declares.</summary>
    public const string ChangeScheme = "CHANGE";

    /// <summary>Which of INSERT, MODIFY or REMOVE this record was.</summary>
    public const string EventNameHeader = "x-amz-ddb-event-name";

    /// <summary>The record's position in the shard, which is what a failure report names.</summary>
    public const string SequenceNumberHeader = "x-amz-ddb-sequence-number";

    /// <summary>The table's stream, in full.</summary>
    public const string StreamArnHeader = "x-amz-ddb-stream-arn";

    /// <summary>How the stream was configured, such as <c>NEW_AND_OLD_IMAGES</c>.</summary>
    public const string StreamViewTypeHeader = "x-amz-ddb-stream-view-type";

    /// <summary>The changes, in the order the shard delivered them.</summary>
    /// <remarks>
    /// Order matters here in a way it does not on a standard queue: a shard promises the changes to
    /// one partition key in the order they happened, and a filter that reordered a batch would
    /// apply a stale write over a fresh one.
    /// </remarks>
    public IReadOnlyList<DynamoDBEvent.DynamodbStreamRecord> Records { get; }

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
    /// duplicate write, and it applies a later change before the replay of an earlier one - which
    /// is the ordering a stream exists to give.
    /// </remarks>
    public BatchFailureMode FailureMode => BatchFailureMode.Checkpoint;

    public IReadOnlyList<int> FailedItems => _failed;

    public void RecordFailure(int index, Exception failure) => _failed.Add(index);

    /// <summary>
    /// The sequence numbers the report has to name.
    /// </summary>
    /// <remarks>
    /// <b>The sequence number, never the event id.</b> Hardened.Amz reported <c>EventID</c> until
    /// 2026-08-15: it identifies a record but is not a position, so Lambda could resolve no
    /// checkpoint from it and silently re-drove the entire batch - the outcome partial batch
    /// reporting exists to avoid, while looking like it worked.
    /// </remarks>
    public IEnumerable<string> FailedSequenceNumbers =>
        _failed.Select(index => Records[index].Dynamodb?.SequenceNumber)
            .Where(number => !string.IsNullOrEmpty(number))!;

    /// <summary>
    /// The request for one change, as a handler will see it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The body is the item, not the record.</b> A handler binds the row that changed, so the
    /// envelope DynamoDB wraps it in - the keys, the view type, the sequence number - becomes
    /// headers and the image becomes the body. That is what lets the same handler shape serve a
    /// queue and a change feed.
    /// </para>
    /// <para>
    /// <b>A REMOVE binds the old image</b>, because there is no new one and a handler told only
    /// that something was deleted cannot say what. The event name header is how a handler that
    /// cares tells the three apart, and <c>[OldImage]</c> reaches the previous row for the other
    /// two.
    /// </para>
    /// </remarks>
    public IExecutionRequest ForRecord(DynamoDBEvent.DynamodbStreamRecord record) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Set(headers, EventNameHeader, record.EventName);
        Set(headers, SequenceNumberHeader, record.Dynamodb?.SequenceNumber);
        Set(headers, StreamArnHeader, record.EventSourceArn);
        Set(headers, StreamViewTypeHeader, record.Dynamodb?.StreamViewType);

        var image = record.Dynamodb?.NewImage ?? record.Dynamodb?.OldImage;

        var body = new MemoryStream();

        using (var writer = new Utf8JsonWriter(body)) {
            AttributeValueJson.WriteItem(writer, image);
        }

        body.Position = 0;

        return new DynamoDbChange(Method, Path, body, headers, record);
    }

    private static void Set(
        IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
