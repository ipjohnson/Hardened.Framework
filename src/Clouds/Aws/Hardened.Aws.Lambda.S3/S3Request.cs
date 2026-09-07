using System.Text.Json;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.S3;

/// <summary>
/// One S3 delivery: a bucket, and the objects that changed in it.
/// </summary>
/// <remarks>
/// <para>
/// Batched like a queue and not like a shard. S3 makes no ordering promise between notifications,
/// so the records are independent and <see cref="FailureMode"/> is <c>PerItem</c>.
/// </para>
/// <para>
/// <b>Nothing is reported back.</b> S3 invokes a function asynchronously and reads no response, so
/// there is no partial batch failure to write and <see cref="ReportsItemFailures"/> is false with no
/// way to turn it on. A failed notification fails the invocation, which is what makes Lambda retry
/// it and eventually send it to the destination or dead letter queue the function was deployed
/// with.
/// </para>
/// </remarks>
public class S3Request : LambdaPayloadRequest, IBatchRequest {
    public S3Request(
        string bucketName,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<S3Notification> records)
        : base(BlobScheme, "/" + bucketName, body, headers) {
        Records = records;
    }

    /// <summary>The scheme an object store routes under, which <c>[Blob]</c> declares.</summary>
    public const string BlobScheme = "BLOB";

    /// <summary>The object's key, decoded.</summary>
    public const string KeyHeader = "x-amz-s3-key";

    /// <summary>Which of ObjectCreated:Put, ObjectRemoved:Delete and the rest this was.</summary>
    public const string EventNameHeader = "x-amz-s3-event-name";

    /// <summary>The object's entity tag, where it has one.</summary>
    public const string ETagHeader = "x-amz-s3-etag";

    /// <summary>How two notifications for one key are ordered.</summary>
    public const string SequencerHeader = "x-amz-s3-sequencer";

    /// <summary>When S3 recorded the change.</summary>
    public const string EventTimeHeader = "x-amz-s3-event-time";

    /// <summary>The bucket, so a handler serving one route can still name it.</summary>
    public const string BucketHeader = "x-amz-s3-bucket";

    /// <summary>The notifications, in the order the envelope carried them.</summary>
    /// <remarks>
    /// Order is preserved and means nothing. S3 states that notifications may arrive out of order,
    /// so a handler that cares compares <c>Sequencer</c> rather than trusting position.
    /// </remarks>
    public IReadOnlyList<S3Notification> Records { get; }

    public int Count => Records.Count;

    public IExecutionRequest ForItem(int index) => ForRecord(Records[index]);

    /// <summary>
    /// False, and not configurable. S3 reads no response from the function it invoked.
    /// </summary>
    public bool ReportsItemFailures => false;

    /// <summary>
    /// Per item, and never consulted while nothing can be reported. Declared honestly rather than
    /// arbitrarily: two objects changing are two independent facts, with no position to rewind to.
    /// </summary>
    public BatchFailureMode FailureMode => BatchFailureMode.PerItem;

    public IReadOnlyList<int> FailedItems => Array.Empty<int>();

    /// <remarks>
    /// Unreachable while <see cref="ReportsItemFailures"/> is false, and throwing says so rather
    /// than letting a caller believe a failure was recorded somewhere.
    /// </remarks>
    public void RecordFailure(int index, Exception failure) =>
        throw new NotSupportedException(
            "S3 reads no response from the function it invoked, so an individual notification " +
            "cannot be reported as failed. The invocation fails instead, which is what makes " +
            "Lambda retry it.");

    /// <summary>
    /// The request for one notification, as a handler will see it.
    /// </summary>
    /// <remarks>
    /// <b>The body is the notification, because there is no object in it.</b> Every other adapter
    /// hands a handler something a publisher wrote; S3 sends metadata and leaves fetching the
    /// object to the handler. So the body is the flattened record - bucket, key, size, etag - and a
    /// handler declares a type with those properties the way it would declare one for a queue
    /// message.
    /// </remarks>
    public IExecutionRequest ForRecord(S3Notification record) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Set(headers, KeyHeader, record.Key);
        Set(headers, EventNameHeader, record.EventName);
        Set(headers, ETagHeader, record.ETag);
        Set(headers, SequencerHeader, record.Sequencer);
        Set(headers, EventTimeHeader, record.EventTime);

        if (Headers.TryGetValue(BucketHeader, out var bucket)) {
            headers[BucketHeader] = bucket;
        }

        var body = new MemoryStream();

        using (var writer = new Utf8JsonWriter(body)) {
            writer.WriteStartObject();
            writer.WriteString("bucket", Path.TrimStart('/'));
            writer.WriteString("key", record.Key);

            if (record.Size is { } size) {
                writer.WriteNumber("size", size);
            }

            writer.WriteString("eTag", record.ETag);
            writer.WriteString("sequencer", record.Sequencer);
            writer.WriteString("eventName", record.EventName);
            writer.WriteString("eventTime", record.EventTime);
            writer.WriteEndObject();
        }

        body.Position = 0;

        return new LambdaPayloadRequest(Method, Path, body, headers);
    }

    private static void Set(
        IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
