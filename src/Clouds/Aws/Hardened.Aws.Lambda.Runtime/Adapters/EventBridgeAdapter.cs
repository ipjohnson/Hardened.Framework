using System.Buffers;
using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// Amazon EventBridge: a scheduled invocation, or one event off a bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one event source here that binds no DTO, and it is not an inconsistency.</b>
/// <c>CloudWatchEvent&lt;TDetail&gt;</c> is generic over the detail type, so binding it means
/// choosing at compile time what every event on the bus contains - which is the handler's own
/// parameter, not the adapter's business. Reading <c>source</c>, <c>detail-type</c> and the raw
/// <c>detail</c> off the document is both less code and the only version that lets two handlers on
/// one bus take different types. Whether a source is worth a DTO is a per-source question.
/// </para>
/// <para>
/// <b>Not batched.</b> EventBridge delivers one event per invocation, so this family member needs
/// no fan-out and works end to end without the batch filter SQS and SNS are waiting on.
/// </para>
/// <para>
/// Routes as <c>TIMER /nightly-rollup</c> for a schedule and <c>EVENT /com.acme.orders/OrderPlaced</c>
/// for everything else. Both come off the delivered event rather than the handler's attribute, so a
/// rule wired to the wrong target is a missing route rather than an event handled by the wrong code.
/// </para>
/// </remarks>
public sealed class EventBridgeAdapter : IPayloadAdapter {
    private const string DetailType = "detail-type";
    private const string Source = "source";
    private const string Detail = "detail";
    private const string Resources = "resources";

    /// <summary>The scheme a scheduled invocation routes under.</summary>
    public const string TimerScheme = "TIMER";

    /// <summary>The scheme a bus event routes under.</summary>
    public const string EventScheme = "EVENT";

    /// <summary>The source EventBridge puts on its own scheduled invocations.</summary>
    public const string ScheduleSource = "aws.events";

    /// <summary>The detail type EventBridge puts on a scheduled invocation.</summary>
    public const string ScheduleDetailType = "Scheduled Event";

    public const string EventIdHeader = "x-amz-event-id";
    public const string EventSourceHeader = "x-amz-event-source";
    public const string EventDetailTypeHeader = "x-amz-event-detail-type";
    public const string EventTimeHeader = "x-amz-event-time";

    /// <remarks>
    /// <c>detail-type</c> is the distinctive field: no other source carries a hyphenated property
    /// at the root, and nothing else pairs one with <c>source</c>. Both are required so a caller's
    /// own payload that happens to have a <c>source</c> is not claimed.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty(DetailType, out var detailType) &&
        detailType.ValueKind == JsonValueKind.String &&
        payload.TryGetProperty(Source, out var source) &&
        source.ValueKind == JsonValueKind.String;

    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var root = payload.Json;

        var source = String(root, Source) ?? "";
        var detailType = String(root, DetailType) ?? "";

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Set(headers, EventIdHeader, String(root, "id"));
        Set(headers, EventSourceHeader, source);
        Set(headers, EventDetailTypeHeader, detailType);
        Set(headers, EventTimeHeader, String(root, "time"));

        var scheduled = source == ScheduleSource && detailType == ScheduleDetailType;

        return new LambdaPayloadRequest(
            scheduled ? TimerScheme : EventScheme,
            scheduled ? "/" + RuleName(root) : "/" + source + "/" + detailType,
            DetailBody(root),
            headers);
    }

    /// <summary>
    /// The rule's own name, off the end of the first resource ARN.
    /// </summary>
    /// <remarks>
    /// <c>arn:aws:events:us-east-1:123456789012:rule/nightly-rollup</c>, or
    /// <c>rule/{bus}/{name}</c> on a bus other than the default - the name is the last segment
    /// either way. A scheduled event with no resources routes to <c>/</c>, which is a missing route
    /// rather than a failure inside the adapter.
    /// </remarks>
    internal static string RuleName(JsonElement root) {
        if (!root.TryGetProperty(Resources, out var resources) ||
            resources.ValueKind != JsonValueKind.Array ||
            resources.GetArrayLength() == 0) {
            return "";
        }

        var arn = resources[0].GetString();

        if (string.IsNullOrEmpty(arn)) {
            return "";
        }

        var slash = arn!.LastIndexOf('/');

        return slash > -1 ? arn.Substring(slash + 1) : arn;
    }

    /// <summary>
    /// The event's <c>detail</c> as UTF-8 bytes, which is what the handler binds its parameter from.
    /// </summary>
    /// <remarks>
    /// Written out through a <see cref="Utf8JsonWriter"/> rather than read as
    /// <c>GetRawText()</c>, which would materialise the detail as a UTF-16 string on its way to
    /// bytes the binder is about to parse as UTF-8 anyway.
    /// </remarks>
    private static Stream DetailBody(JsonElement root) {
        if (!root.TryGetProperty(Detail, out var detail) ||
            detail.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
            return Stream.Null;
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer)) {
            detail.WriteTo(writer);
        }

        return new MemoryStream(buffer.WrittenSpan.ToArray(), writable: false);
    }

    /// <summary>
    /// Rethrown, so a failed run is recorded as failed against the rule rather than as a success
    /// that produced nothing.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// Nothing. EventBridge reads no response from a target.
    /// </summary>
    public ValueTask WriteResponse(IExecutionContext context, Stream output) => default;

    private static string? String(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
