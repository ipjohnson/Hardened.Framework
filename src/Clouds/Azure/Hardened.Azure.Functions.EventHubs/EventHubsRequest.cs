using System.Globalization;
using Azure.Messaging.EventHubs;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.EventHubs;

/// <summary>
/// One Event Hubs delivery: a hub, and the events one partition carried.
/// </summary>
/// <remarks>
/// <para>
/// One request for the batch, forked per event, the same as a queue delivery - the function is
/// bound to one hub, so the route is decided once. Where it stops resembling a queue is the
/// failure: a partition is an ordered log and the host checkpoints it by position, so the run
/// stops at the first failure rather than attempting the rest.
/// </para>
/// <para>
/// <b>Nothing is reported back.</b> The host checkpoints the whole batch when the invocation
/// succeeds and leaves the checkpoint where it was when it fails; there is no report naming a
/// position, which is the difference from Kinesis. So <see cref="ReportsItemFailures"/> is false
/// with no way to turn it on, and a failed event fails the invocation.
/// </para>
/// </remarks>
public class EventHubsRequest : FunctionsPayloadRequest, IBatchRequest {
    public EventHubsRequest(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<EventData> events)
        : base(scheme, path, body, headers) {
        Events = events;
    }

    /// <summary>The scheme a stream routes under, which <c>[Stream]</c> declares.</summary>
    public const string StreamScheme = "STREAM";

    /// <summary>The event's position in the partition.</summary>
    public const string SequenceNumberHeader = "x-azure-eventhubs-sequence-number";

    /// <summary>The event's offset in the partition, which is what a checkpoint is made of.</summary>
    public const string OffsetHeader = "x-azure-eventhubs-offset";

    /// <summary>What the publisher chose to order by, when it chose.</summary>
    public const string PartitionKeyHeader = "x-azure-eventhubs-partition-key";

    /// <summary>When the hub accepted the event, ISO 8601.</summary>
    public const string EnqueuedTimeHeader = "x-azure-eventhubs-enqueued-time";

    /// <summary>The publisher's message id, when it set one.</summary>
    public const string MessageIdHeader = "x-azure-eventhubs-message-id";

    /// <summary>The events, in the order the partition delivered them.</summary>
    /// <remarks>
    /// Order is the point here in a way it is not on a queue. A partition promises the events
    /// written under one partition key in the order they were written, so a filter that reordered
    /// a batch would break the guarantee the hub exists to give.
    /// </remarks>
    public IReadOnlyList<EventData> Events { get; }

    public int Count => Events.Count;

    public IExecutionRequest ForItem(int index) => ForEvent(Events[index]);

    /// <summary>False, and not configurable: the host reads no report from an Event Hubs function.</summary>
    public bool ReportsItemFailures => false;

    /// <summary>
    /// By checkpoint, because a partition is a log. Declared honestly although nothing reports:
    /// it says what a filter should do with the events after a failure, and running them first
    /// would apply a later event before the replay of an earlier one.
    /// </summary>
    public BatchFailureMode FailureMode => BatchFailureMode.Checkpoint;

    public IReadOnlyList<int> FailedItems => Array.Empty<int>();

    /// <remarks>
    /// Unreachable while <see cref="ReportsItemFailures"/> is false, and throwing says so rather
    /// than letting a caller believe a failure was recorded somewhere.
    /// </remarks>
    public void RecordFailure(int index, Exception failure) =>
        throw new NotSupportedException(
            "The host reads no report from an Event Hubs function, so an individual event cannot " +
            "be reported as failed. The invocation fails instead, which leaves the checkpoint where " +
            "it was.");

    /// <summary>
    /// The request for one event, as a handler will see it.
    /// </summary>
    /// <remarks>
    /// The body is the event's own bytes and nothing else. Event Hubs carries a blob it knows
    /// nothing about, so the handler binds the publisher's bytes against its own parameter type,
    /// the arrangement Kinesis has. The publisher's properties become headers under their own
    /// names, and the facts the hub carries outside them get prefixed names.
    /// </remarks>
    public IExecutionRequest ForEvent(EventData eventData) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in eventData.Properties) {
            var rendered = Render(property.Value);

            if (rendered != null) {
                headers[property.Key] = rendered;
            }
        }

        headers[SequenceNumberHeader] = eventData.SequenceNumber.ToString(CultureInfo.InvariantCulture);
        headers[OffsetHeader] = eventData.Offset.ToString(CultureInfo.InvariantCulture);
        Set(headers, PartitionKeyHeader, eventData.PartitionKey);
        headers[EnqueuedTimeHeader] = eventData.EnqueuedTime.ToString("o", CultureInfo.InvariantCulture);
        Set(headers, MessageIdHeader, eventData.MessageId);
        Set(headers, "Content-Type", eventData.ContentType);

        var body = eventData.EventBody;

        return new FunctionsPayloadRequest(
            Method,
            Path,
            body == null || body.ToMemory().IsEmpty ? Stream.Null : body.ToStream(),
            headers);
    }

    private static string? Render(object? value) =>
        value switch {
            null => null,
            string text => text,
            byte[] => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
