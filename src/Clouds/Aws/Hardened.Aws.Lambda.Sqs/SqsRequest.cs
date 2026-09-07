using System.Text;
using Amazon.Lambda.SQSEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Sqs;

/// <summary>
/// One SQS delivery: a queue, and the messages it carried.
/// </summary>
/// <remarks>
/// <para>
/// <b>One request for the batch, not one per message.</b> An event source mapping is attached to a
/// single queue, so every message in an invocation routes to the same handler - the routing
/// decision is made once and the fan-out is a pipeline concern rather than an adapter one. That is
/// what keeps <c>IPayloadAdapter.CreateRequest</c> returning a single request: an adapter that had
/// to return many would force every other adapter into a collection it has no use for.
/// </para>
/// <para>
/// <see cref="Records"/> is how the batch filter reaches the messages without deserializing the
/// payload a second time. It forks this request per record through <c>Clone</c> and replaces the
/// body, which is why <c>Clone</c> here returns the plain payload request: a fork is one message,
/// and one message has no batch.
/// </para>
/// </remarks>
public class SqsRequest : LambdaPayloadRequest, IBatchRequest {
    private readonly List<int> _failed = [];

    public SqsRequest(
        string queueName,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<SQSEvent.SQSMessage> records,
        bool reportsItemFailures = false)
        : base(SqsScheme, "/" + queueName, body, headers) {
        Records = records;
        ReportsItemFailures = reportsItemFailures;
    }

    /// <summary>
    /// The scheme SQS routes under. Not an HTTP verb, and it never was one - the method slot on a
    /// request has always been a string.
    /// </summary>
    public const string SqsScheme = "QUEUE";

    /// <summary>
    /// The messages, in the order the queue delivered them.
    /// </summary>
    /// <remarks>
    /// Order is worth preserving even on a standard queue, which does not promise it: a FIFO queue
    /// does, and a filter that reordered a batch would break the one case the ordering matters in
    /// while looking correct in the other.
    /// </remarks>
    public IReadOnlyList<SQSEvent.SQSMessage> Records { get; }

    /// <summary>The message id, as a header, so a handler can log or deduplicate on it.</summary>
    public const string MessageIdHeader = "x-amz-sqs-message-id";

    /// <summary>The receipt handle, which is what a handler needs to delete or extend a message.</summary>
    public const string ReceiptHandleHeader = "x-amz-sqs-receipt-handle";

    /// <summary>The queue the batch came from, in full.</summary>
    public const string QueueArnHeader = "x-amz-sqs-queue-arn";

    /// <summary>
    /// The request for one message in the batch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fan-out, and it lives here rather than in the batch filter because it is the
    /// mapping from an SQS message to a request - which is adapter knowledge. The filter decides
    /// <em>when</em> to fork and what to do when a fork throws; it should not also have to know
    /// that a message attribute is a header and a receipt handle is not.
    /// </para>
    /// <para>
    /// The message's own attributes become headers under their own names, and the three facts SQS
    /// carries outside them get prefixed names so a user attribute called <c>messageId</c> cannot
    /// collide with the message id.
    /// </para>
    /// </remarks>
    public int Count => Records.Count;

    public IExecutionRequest ForItem(int index) => ForRecord(Records[index]);

    /// <summary>
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>.
    /// </summary>
    /// <remarks>
    /// Off unless the deployment says otherwise, and the default is not timidity. A
    /// <c>batchItemFailures</c> report sent to a mapping that did not ask for one is discarded and
    /// the whole batch is marked successful, so guessing wrong loses every failed message silently.
    /// Guessing wrong the other way redelivers messages that already succeeded, which a handler is
    /// required to tolerate anyway - SQS gives at-least-once delivery whatever this is set to.
    /// </remarks>
    public bool ReportsItemFailures { get; }

    /// <summary>
    /// Per item, because a queue has no order to keep and the report names exactly what to
    /// redeliver.
    /// </summary>
    /// <remarks>
    /// True even of a FIFO queue. A message group is ordered and SQS stops delivering a group once
    /// one of its messages is in flight and failing, so ordering is enforced by the queue rather
    /// than by what this batch does after a failure - and a standard queue's messages are
    /// independent, which is the case worth optimising for.
    /// </remarks>
    public BatchFailureMode FailureMode => BatchFailureMode.PerItem;

    public IReadOnlyList<int> FailedItems => _failed;

    public void RecordFailure(int index, Exception failure) => _failed.Add(index);

    /// <summary>
    /// The message ids the report has to name, which is what SQS keys a partial failure on.
    /// </summary>
    public IEnumerable<string> FailedMessageIds =>
        _failed.Select(index => Records[index].MessageId).Where(id => !string.IsNullOrEmpty(id));

    public IExecutionRequest ForRecord(SQSEvent.SQSMessage record) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (record.MessageAttributes != null) {
            foreach (var attribute in record.MessageAttributes) {
                // StringValue only. A binary attribute is not a header, and rendering one as
                // base64 under the same name would make a handler unable to tell the two apart.
                if (attribute.Value?.StringValue is { } value) {
                    headers[attribute.Key] = value;
                }
            }
        }

        Set(headers, MessageIdHeader, record.MessageId);
        Set(headers, ReceiptHandleHeader, record.ReceiptHandle);
        Set(headers, QueueArnHeader, record.EventSourceArn);

        return new LambdaPayloadRequest(Method, Path, BodyStream(record.Body), headers);
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }

    /// <summary>
    /// The message body as a stream. Empty rather than null for an empty message, so a handler
    /// binding a body sees nothing to bind rather than a null reference.
    /// </summary>
    private static Stream BodyStream(string? body) =>
        string.IsNullOrEmpty(body)
            ? Stream.Null
            : new MemoryStream(Encoding.UTF8.GetBytes(body!), writable: false);
}
