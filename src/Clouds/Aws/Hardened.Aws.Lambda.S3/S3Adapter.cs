using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.S3;

/// <summary>
/// Amazon S3 object notifications, delivered against one bucket.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a notification and a route. A throw is rethrown rather than
/// answered, because there is no caller on the other end - failing the invocation is what makes
/// Lambda retry it and, eventually, send it where the function's failure destination points.
/// </para>
/// <para>
/// Routes as <c>BLOB /uploads</c>, taking the bucket's name from the event rather than from the
/// handler's attribute, so a notification configuration wired to the wrong function is a missing
/// route rather than an object handled by the wrong code.
/// </para>
/// <para>
/// <b>Worth knowing before reaching for this adapter.</b> AWS now recommends routing S3 events
/// through EventBridge rather than invoking a function directly, and the EventBridge adapter
/// already serves that. Direct notification is the older path and is still what a bucket configured
/// with a Lambda target sends, so this exists to serve it rather than to be the recommendation.
/// </para>
/// <para>
/// <b>No Amazon event package.</b> <c>Amazon.Lambda.S3Events</c> has no dependencies and would have
/// been safe to take, unlike the Kinesis one. It is left out because six documented fields do not
/// need 27.1 KB of models to name them, and the handler binds a flattened projection either way.
/// </para>
/// </remarks>
public sealed class S3Adapter : IPayloadAdapter {
    private const string EventSource = "eventSource";
    private const string Records = "Records";

    /// <summary>The value S3 puts in every record's <c>eventSource</c>.</summary>
    public const string EventSourceValue = "aws:s3";

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
        var root = payload.Json;

        var records = new List<S3Notification>();
        string? bucket = null;

        if (root.TryGetProperty(Records, out var array) &&
            array.ValueKind == JsonValueKind.Array) {
            foreach (var element in array.EnumerateArray()) {
                if (!element.TryGetProperty("s3", out var s3) ||
                    s3.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                bucket ??= s3.TryGetProperty("bucket", out var b) ? String(b, "name") : null;

                var obj = s3.TryGetProperty("object", out var o) &&
                          o.ValueKind == JsonValueKind.Object
                    ? o
                    : default;

                records.Add(new S3Notification(
                    DecodeKey(String(obj, "key") ?? ""),
                    Size(obj),
                    String(obj, "eTag"),
                    String(obj, "sequencer"),
                    String(element, "eventName") ?? "",
                    String(element, "eventTime")));
            }
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(bucket)) {
            headers[S3Request.BucketHeader] = bucket;
        }

        return new S3Request(
            bucket ?? "",
            new MemoryStream(payload.Raw.ToArray(), writable: false),
            headers,
            records);
    }

    /// <summary>
    /// The object's key, as the object is actually named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>S3 URL-encodes the key in a notification and this is the classic way to lose an
    /// object.</b> A key of <c>my report.pdf</c> arrives as <c>my+report.pdf</c>, and a handler
    /// passing that straight back to <c>GetObject</c> asks for a key that does not exist. Non-ASCII
    /// keys arrive percent-encoded and fail the same way.
    /// </para>
    /// <para>
    /// <c>UrlDecode</c> rather than <c>UnescapeDataString</c>, because the plus sign is the whole
    /// problem: S3 uses form encoding, where <c>+</c> means a space, and
    /// <c>Uri.UnescapeDataString</c> leaves it alone. A key that genuinely contains a plus arrives
    /// as <c>%2B</c> and decodes correctly either way.
    /// </para>
    /// </remarks>
    internal static string DecodeKey(string key) =>
        key.Length == 0 ? key : System.Net.WebUtility.UrlDecode(key.Replace("+", "%20"));

    /// <remarks>
    /// Absent on a delete, because there is no object left to have one - so null rather than zero,
    /// which a handler could not tell from an empty object.
    /// </remarks>
    private static long? Size(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty("size", out var size) &&
        size.ValueKind == JsonValueKind.Number &&
        size.TryGetInt64(out var value)
            ? value
            : null;

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Rethrown. Failing the invocation is the only signal S3 and Lambda take from a function that
    /// could not handle a notification.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    public IExecutionResponse CreateResponse(Stream output) => new LambdaPayloadResponse(output);

    /// <summary>
    /// Nothing. S3 invokes asynchronously and reads no response, so anything written here would be
    /// discarded - and writing an empty failure report would suggest a reporting protocol that does
    /// not exist.
    /// </summary>
    public ValueTask WriteResponse(IExecutionContext context, Stream output) => default;
}
