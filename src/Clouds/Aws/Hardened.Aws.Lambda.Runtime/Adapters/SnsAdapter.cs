using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Serialization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// Amazon SNS, delivered as a notification against one topic.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped, and a rethrow rather than an answer for the same reason SQS is: failing the
/// invocation is how a notification gets retried.
/// </para>
/// <para>
/// Routes as <c>TOPIC /order-events</c>, the topic name taken from the delivered event.
/// </para>
/// </remarks>
public sealed class SnsAdapter : IPayloadAdapter {
    /// <summary>
    /// The field SNS puts its event source in, capitalised where SQS, DynamoDB Streams and Kinesis
    /// all use a lower-case first letter.
    /// </summary>
    /// <remarks>
    /// AWS's inconsistency, not a typo. An adapter reading <c>eventSource</c> here matches nothing
    /// and the notification falls through to whatever claims it next.
    /// </remarks>
    private const string EventSource = "EventSource";

    /// <summary>The value SNS puts in every record's <c>EventSource</c>.</summary>
    public const string EventSourceValue = "aws:sns";

    /// <remarks>
    /// The value rather than the presence of <c>Records</c>, which four different sources use.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        SqsAdapter.FirstRecord(payload) is { } record &&
        record.TryGetProperty(EventSource, out var source) &&
        source.ValueKind == JsonValueKind.String &&
        source.ValueEquals(EventSourceValue);

    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var batch = JsonSerializer.Deserialize(
                        payload.Raw.Span, SnsSerializerContext.Default.SNSEvent)
                    ?? throw new InvalidOperationException(
                        "The SNS adapter was given a payload that deserialized to null. The peek " +
                        "identified it by its records' aws:sns event source, so this is a " +
                        "malformed event rather than a different source.");

        // Copied rather than cast: SNSEvent.Records is IList<T>, which does not implement
        // IReadOnlyList<T>, and the copy is also what stops a caller mutating the batch a filter is
        // partway through forking.
        var records = batch.Records == null
            ? Array.Empty<SNSEvent.SNSRecord>()
            : batch.Records.ToArray();

        return new SnsRequest(
            TopicName(records.Length > 0 ? records[0].Sns?.TopicArn : null),
            new MemoryStream(payload.Raw.ToArray(), writable: false),
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            records);
    }

    /// <summary>
    /// The topic's own name, off the end of the ARN.
    /// </summary>
    /// <remarks>
    /// <c>arn:aws:sns:us-east-1:123456789012:order-events</c>. A subscription ARN has a further
    /// segment holding the subscription's own id, which is why this reads the topic ARN inside the
    /// notification rather than <c>EventSubscriptionArn</c> beside it.
    /// </remarks>
    internal static string TopicName(string? topicArn) {
        if (string.IsNullOrEmpty(topicArn)) {
            return "";
        }

        var colon = topicArn!.LastIndexOf(':');

        return colon > -1 ? topicArn.Substring(colon + 1) : topicArn;
    }

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// Nothing. SNS reads no response - a delivery either succeeded or the invocation failed.
    /// </summary>
    public ValueTask WriteResponse(IExecutionContext context, Stream output) => default;
}
