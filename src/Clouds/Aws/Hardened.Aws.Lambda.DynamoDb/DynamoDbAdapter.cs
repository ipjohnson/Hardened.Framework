using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// DynamoDB Streams, delivered as an ordered batch against one table's shard.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a row and a route. A throw is rethrown rather than answered,
/// because there is no caller on the other end of a connection - failing the invocation is what
/// makes Lambda replay the shard.
/// </para>
/// <para>
/// Routes as <c>CHANGE /orders</c>, taking the table's name from the stream ARN rather than from the
/// handler's attribute. The attribute says which table a handler wants; the ARN says which table
/// actually delivered, and routing on the latter makes a wrong event source mapping a missing route
/// rather than a row handled by the wrong code.
/// </para>
/// </remarks>
public sealed class DynamoDbAdapter : IPayloadAdapter {
    private readonly bool _reportsItemFailures;

    /// <param name="reportsItemFailures">
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>.
    /// </param>
    public DynamoDbAdapter(bool reportsItemFailures = false) {
        _reportsItemFailures = reportsItemFailures;
    }

    private const string EventSource = "eventSource";

    /// <summary>The value DynamoDB Streams puts in every record's <c>eventSource</c>.</summary>
    public const string EventSourceValue = "aws:dynamodb";

    /// <remarks>
    /// The value, not the presence of <c>Records</c>. See <see cref="LambdaPayload.FirstRecord"/>
    /// for why the array alone recognises nothing.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        LambdaPayload.FirstRecord(payload) is { } record &&
        record.TryGetProperty(EventSource, out var source) &&
        source.ValueKind == JsonValueKind.String &&
        source.ValueEquals(EventSourceValue);

    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var batch = JsonSerializer.Deserialize(
                        payload.Raw.Span, DynamoDbEventJson.Event)
                    ?? throw new InvalidOperationException(
                        "The DynamoDB adapter was given a payload that deserialized to null. The " +
                        "peek identified it by its records' aws:dynamodb event source, so this is " +
                        "a malformed event rather than a different source.");

        // Copied to a list because the package models Records as IList, which is not an
        // IReadOnlyList - and the request holds it for the life of the invocation.
        var records = batch.Records?.ToList() ?? [];

        return new DynamoDbRequest(
            TableName(records.Count > 0 ? records[0].EventSourceArn : null),
            new MemoryStream(payload.Raw.ToArray(), writable: false),
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            records,
            _reportsItemFailures);
    }

    /// <summary>
    /// The table's own name, out of the middle of the stream ARN.
    /// </summary>
    /// <remarks>
    /// <c>arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-01-01T00:00:00.000</c>
    /// carries the name between <c>table/</c> and <c>/stream</c>, so unlike a queue's ARN the answer
    /// is not the last segment - the last segment is the timestamp the stream was enabled at, which
    /// changes when a table's stream is turned off and on again. Anything that is not a stream ARN
    /// is returned whole, and an absent one gives an empty name; both produce a route no handler
    /// declared, which the not-found handler reports with the value in hand.
    /// </remarks>
    internal static string TableName(string? eventSourceArn) {
        if (string.IsNullOrEmpty(eventSourceArn)) {
            return "";
        }

        const string marker = ":table/";

        var start = eventSourceArn!.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0) {
            return eventSourceArn;
        }

        start += marker.Length;

        var end = eventSourceArn.IndexOf('/', start);

        return end < 0
            ? eventSourceArn.Substring(start)
            : eventSourceArn.Substring(start, end - start);
    }

    /// <summary>
    /// Rethrown. Failing the invocation is what replays the shard - answering would advance the
    /// checkpoint past a change nothing handled.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// The partial batch failure report, keyed on sequence number.
    /// </summary>
    /// <remarks>
    /// Always written, and empty when every change succeeded. Under
    /// <c>BatchFailureMode.Checkpoint</c> the batch stops at the first failure, so this names at
    /// most one - which is the point: Lambda rewinds to it and redelivers from there.
    /// </remarks>
    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        await using var writer = new Utf8JsonWriter(output);

        writer.WriteStartObject();
        writer.WriteStartArray("batchItemFailures");

        if (context.Request is DynamoDbRequest batch) {
            foreach (var sequenceNumber in batch.FailedSequenceNumbers) {
                writer.WriteStartObject();
                writer.WriteString("itemIdentifier", sequenceNumber);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync();
    }
}
