using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Kinesis;

/// <summary>
/// Kinesis Data Streams, delivered as an ordered batch off one shard.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a record and a route. A throw is rethrown rather than answered,
/// because there is no caller on the other end of a connection - failing the invocation is what
/// makes Lambda replay the shard.
/// </para>
/// <para>
/// Routes as <c>STREAM /clickstream</c>, taking the stream's name from the event's ARN rather than
/// from the handler's attribute. The attribute says which stream a handler wants; the ARN says
/// which one actually delivered, and routing on the latter makes a wrong event source mapping a
/// missing route rather than records handled by the wrong code.
/// </para>
/// <para>
/// <b>No Amazon event package.</b> <c>Amazon.Lambda.KinesisEvents</c> is 27.6 KB and depends on
/// <c>AWSSDK.Kinesis</c> and <c>AWSSDK.Core</c>, 1094 KB between them - three times the whole of
/// Hardened after trimming - to name five documented fields on an envelope whose payload is an
/// opaque blob. The EventBridge adapter reached the same conclusion about its own envelope.
/// </para>
/// </remarks>
public sealed class KinesisAdapter : IPayloadAdapter {
    private readonly bool _reportsItemFailures;

    /// <param name="reportsItemFailures">
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>.
    /// </param>
    public KinesisAdapter(bool reportsItemFailures = false) {
        _reportsItemFailures = reportsItemFailures;
    }

    private const string EventSource = "eventSource";
    private const string Kinesis = "kinesis";
    private const string Records = "Records";

    /// <summary>The value Kinesis puts in every record's <c>eventSource</c>.</summary>
    public const string EventSourceValue = "aws:kinesis";

    /// <remarks>
    /// The value, not the presence of <c>Records</c>. See <see cref="LambdaPayload.FirstRecord"/>
    /// for why the array alone recognises nothing.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        LambdaPayload.FirstRecord(payload) is { } record &&
        record.TryGetProperty(EventSource, out var source) &&
        source.ValueKind == JsonValueKind.String &&
        source.ValueEquals(EventSourceValue);

    /// <remarks>
    /// Read off the parsed document rather than deserialized into a model. The peek has already
    /// paid for the parse, and every field wanted here is a string on it.
    /// </remarks>
    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var root = payload.Json;

        var records = new List<KinesisRecord>();
        string? arn = null;

        if (root.TryGetProperty(Records, out var array) &&
            array.ValueKind == JsonValueKind.Array) {
            foreach (var element in array.EnumerateArray()) {
                arn ??= String(element, "eventSourceARN");

                if (!element.TryGetProperty(Kinesis, out var inner) ||
                    inner.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                records.Add(new KinesisRecord(
                    Data(inner),
                    String(inner, "sequenceNumber") ?? "",
                    String(inner, "partitionKey") ?? "",
                    String(element, "eventID") ?? "",
                    Number(inner, "approximateArrivalTimestamp")));
            }
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(arn)) {
            headers[KinesisRequest.StreamArnHeader] = arn;
        }

        return new KinesisRequest(
            StreamName(arn),
            new MemoryStream(payload.Raw.ToArray(), writable: false),
            headers,
            records,
            _reportsItemFailures);
    }

    /// <summary>
    /// The record's payload, base64-decoded.
    /// </summary>
    /// <remarks>
    /// <c>GetBytesFromBase64</c> rather than reading the string and decoding it, so the bytes come
    /// out of the document without a string in between. Undecodable data gives an empty record
    /// rather than failing the batch: Kinesis will not produce one, and a malformed record is the
    /// handler's to reject with the rest of the batch still reported.
    /// </remarks>
    private static ReadOnlyMemory<byte> Data(JsonElement kinesis) {
        if (!kinesis.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.String) {
            return ReadOnlyMemory<byte>.Empty;
        }

        return data.TryGetBytesFromBase64(out var bytes)
            ? bytes
            : ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// The stream's own name, off the end of the ARN.
    /// </summary>
    /// <remarks>
    /// <c>arn:aws:kinesis:us-east-1:123456789012:stream/clickstream</c> ends in the name. Anything
    /// that is not a stream ARN is returned whole and an absent one gives an empty name; both
    /// produce a route no handler declared, which the not-found handler reports with the value in
    /// hand rather than failing inside the adapter.
    /// </remarks>
    internal static string StreamName(string? eventSourceArn) {
        if (string.IsNullOrEmpty(eventSourceArn)) {
            return "";
        }

        var slash = eventSourceArn!.LastIndexOf('/');

        return slash > -1 ? eventSourceArn.Substring(slash + 1) : eventSourceArn;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <remarks>
    /// As its text. <c>approximateArrivalTimestamp</c> is epoch seconds with a fraction, and the
    /// header carries what the envelope said rather than a rendering of it - which also sidesteps
    /// the trap the DynamoDB models fall into, where the field is typed <c>DateTime</c> and the
    /// wire form is a number.
    /// </remarks>
    private static string? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;

    /// <summary>
    /// Rethrown. Failing the invocation is what replays the shard - answering would advance the
    /// checkpoint past a record nothing handled.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// The partial batch failure report, keyed on sequence number.
    /// </summary>
    /// <remarks>
    /// Always written, and empty when every record succeeded. Under
    /// <c>BatchFailureMode.Checkpoint</c> the batch stops at the first failure, so this names at
    /// most one - which is the point: Lambda rewinds to it and redelivers from there.
    /// </remarks>
    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        await using var writer = new Utf8JsonWriter(output);

        writer.WriteStartObject();
        writer.WriteStartArray("batchItemFailures");

        if (context.Request is KinesisRequest batch) {
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
