using System.Text;
using Amazon.Lambda.SNSEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Sns;

/// <summary>
/// One SNS delivery: a topic, and the notifications it carried.
/// </summary>
/// <remarks>
/// <para>
/// SNS delivers a single record per invocation, but the payload is an array and this treats it as
/// one. Reading <c>Records[0]</c> and ignoring the rest would silently drop notifications the day
/// that changes, and the batch filter this shares with SQS costs nothing to reuse.
/// </para>
/// <para>
/// <b>There is no partial failure report here.</b> SNS has no equivalent of
/// <c>batchItemFailures</c>: a throw fails the invocation and SNS retries the whole delivery
/// according to the subscription's policy, then sends it to the dead letter queue. That is a
/// difference in the response contract rather than in the shape, which is why SNS and SQS share a
/// family and not an adapter.
/// </para>
/// </remarks>
public class SnsRequest : LambdaPayloadRequest, IBatchRequest {
    public SnsRequest(
        string topicName,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<SNSEvent.SNSRecord> records)
        : base(TopicScheme, "/" + topicName, body, headers) {
        Records = records;
    }

    /// <summary>The scheme SNS routes under.</summary>
    public const string TopicScheme = "TOPIC";

    /// <summary>The message id SNS assigned, as a header.</summary>
    public const string MessageIdHeader = "x-amz-sns-message-id";

    /// <summary>The subject, when the publisher set one. Absent rather than empty when they did not.</summary>
    public const string SubjectHeader = "x-amz-sns-subject";

    /// <summary>The topic the notification was published to, in full.</summary>
    public const string TopicArnHeader = "x-amz-sns-topic-arn";

    public IReadOnlyList<SNSEvent.SNSRecord> Records { get; }

    public int Count => Records.Count;

    public IExecutionRequest ForItem(int index) => ForRecord(Records[index]);

    /// <summary>
    /// Never. SNS has no per-notification failure report, so a failed handler must fail the
    /// invocation - that is what makes SNS retry the delivery and eventually route it to the
    /// subscription's dead letter queue.
    /// </summary>
    public bool ReportsItemFailures => false;

    public IReadOnlyList<int> FailedItems => Array.Empty<int>();

    /// <summary>
    /// Unreachable. The filter rethrows rather than recording when
    /// <see cref="ReportsItemFailures"/> is false, and throwing here says so rather than letting a
    /// future change record failures into a list nothing reads.
    /// </summary>
    public void RecordFailure(int index, Exception failure) =>
        throw new NotSupportedException(
            "SNS has no per-notification failure report. A failed notification fails the " +
            "invocation, which is what makes SNS redeliver it.");

    /// <summary>
    /// The request for one notification.
    /// </summary>
    /// <remarks>
    /// The body is <c>Sns.Message</c> rather than the record, because the message is what the
    /// publisher sent and the rest is envelope. A handler binding its own type gets what was
    /// published, not what SNS wrapped it in.
    /// </remarks>
    public IExecutionRequest ForRecord(SNSEvent.SNSRecord record) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var message = record.Sns;

        if (message?.MessageAttributes != null) {
            foreach (var attribute in message.MessageAttributes) {
                // String attributes only, as with SQS: a Binary attribute is not a header.
                if (attribute.Value?.Value is { } value &&
                    string.Equals(attribute.Value.Type, "String", StringComparison.Ordinal)) {
                    headers[attribute.Key] = value;
                }
            }
        }

        Set(headers, MessageIdHeader, message?.MessageId);
        Set(headers, SubjectHeader, message?.Subject);
        Set(headers, TopicArnHeader, message?.TopicArn);

        return new LambdaPayloadRequest(Method, Path, BodyStream(message?.Message), headers);
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }

    private static Stream BodyStream(string? body) =>
        string.IsNullOrEmpty(body)
            ? Stream.Null
            : new MemoryStream(Encoding.UTF8.GetBytes(body!), writable: false);
}
