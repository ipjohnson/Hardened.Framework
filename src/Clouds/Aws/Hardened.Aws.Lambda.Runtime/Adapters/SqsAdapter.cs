using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Serialization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// Amazon SQS, delivered as a batch against one queue.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a record and a route, no path template and no connection. A
/// throw is rethrown rather than answered, because there is no caller waiting on the other end of a
/// connection - failing the invocation is what returns the message to the queue.
/// </para>
/// <para>
/// Routes as <c>QUEUE /orders-new</c>, taking the queue's name from the event rather than from the
/// handler's attribute. The attribute says which queue a handler wants; the event says which queue
/// actually delivered, and routing on the latter is what makes a wrong event source mapping a
/// missing route rather than a message handled by the wrong code.
/// </para>
/// </remarks>
public sealed class SqsAdapter : IPayloadAdapter {
    private const string Records = "Records";
    private const string EventSource = "eventSource";

    /// <summary>The value SQS puts in every record's <c>eventSource</c>.</summary>
    public const string EventSourceValue = "aws:sqs";

    /// <remarks>
    /// <para>
    /// The <em>value</em>, not the presence of <c>Records</c>. SQS, SNS, DynamoDB Streams and
    /// Kinesis all arrive as a <c>Records</c> array, so an adapter matching on the array alone
    /// claims all four and whichever is asked first wins - which is exactly the order dependence
    /// the design removed.
    /// </para>
    /// <para>
    /// An empty array declines rather than throwing. AWS does not invoke with an empty batch, and a
    /// payload no adapter claims is an error the dispatcher raises by name.
    /// </para>
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        FirstRecord(payload) is { } record &&
        record.TryGetProperty(EventSource, out var source) &&
        source.ValueKind == JsonValueKind.String &&
        source.ValueEquals(EventSourceValue);

    internal static JsonElement? FirstRecord(JsonElement payload) {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(Records, out var records) ||
            records.ValueKind != JsonValueKind.Array ||
            records.GetArrayLength() == 0) {
            return null;
        }

        var first = records[0];

        return first.ValueKind == JsonValueKind.Object ? first : null;
    }

    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var batch = JsonSerializer.Deserialize(
                        payload.Raw.Span, SqsSerializerContext.Default.SQSEvent)
                    ?? throw new InvalidOperationException(
                        "The SQS adapter was given a payload that deserialized to null. The peek " +
                        "identified it by its records' aws:sqs event source, so this is a " +
                        "malformed event rather than a different source.");

        var records = batch.Records ?? new List<SQSEvent.SQSMessage>();

        return new SqsRequest(
            QueueName(records.Count > 0 ? records[0].EventSourceArn : null),
            // The whole payload, because this request is the batch. A per-record body arrives on
            // the forks SqsRequest.ForRecord builds.
            new MemoryStream(payload.Raw.ToArray(), writable: false),
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(
                StringComparer.OrdinalIgnoreCase),
            records);
    }

    /// <summary>
    /// The queue's own name, off the end of the ARN.
    /// </summary>
    /// <remarks>
    /// <c>arn:aws:sqs:us-east-1:123456789012:orders-new</c> ends in the name, which is the only
    /// part of it a handler declared. Anything that is not an ARN is returned whole, and an absent
    /// one gives an empty name - both produce a route no handler declared, which the not-found
    /// handler reports with the value in hand rather than failing inside the adapter.
    /// </remarks>
    internal static string QueueName(string? eventSourceArn) {
        if (string.IsNullOrEmpty(eventSourceArn)) {
            return "";
        }

        var colon = eventSourceArn!.LastIndexOf(':');

        return colon > -1 ? eventSourceArn.Substring(colon + 1) : eventSourceArn;
    }

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// The partial batch failure report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always written, and empty until the batch filter has failures to name. An empty list means
    /// every message succeeded, which is the correct report for an invocation that got this far -
    /// a throw is rethrown by this family, so a failed handler fails the invocation and never
    /// reaches here.
    /// </para>
    /// <para>
    /// Harmless when the event source mapping has no <c>ReportBatchItemFailures</c>: the response
    /// of an SQS invocation is ignored unless the mapping asked for it.
    /// </para>
    /// </remarks>
    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        await using var writer = new Utf8JsonWriter(output);

        writer.WriteStartObject();
        writer.WriteStartArray("batchItemFailures");
        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync();
    }
}
