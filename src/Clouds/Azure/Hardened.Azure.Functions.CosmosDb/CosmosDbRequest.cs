using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.CosmosDb;

/// <summary>
/// One change feed delivery: a container, and the documents that changed in it.
/// </summary>
/// <remarks>
/// <para>
/// One request for the batch, forked per document, the same as a queue delivery - the function
/// is bound to one container, so the route is decided once. Where it stops resembling a queue is
/// the failure: the feed is an ordered log per partition and the extension advances its lease by
/// position, so the run stops at the first failure rather than attempting the rest.
/// </para>
/// <para>
/// <b>Nothing is reported back.</b> The extension advances the lease when the invocation succeeds
/// and leaves it when the invocation fails; there is no report naming a position. So
/// <see cref="ReportsItemFailures"/> is false with no way to turn it on, and a failed change fails
/// the invocation.
/// </para>
/// </remarks>
public class CosmosDbRequest : FunctionsPayloadRequest, IBatchRequest {
    public CosmosDbRequest(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<CosmosDbDocument> documents)
        : base(scheme, path, body, headers) {
        Documents = documents;
    }

    /// <summary>The document's <c>id</c>, as a header, so a handler can log or deduplicate on it.</summary>
    public const string IdHeader = "x-azure-cosmos-id";

    /// <summary>The change's log sequence number, which is the position the feed is read by.</summary>
    public const string LsnHeader = "x-azure-cosmos-lsn";

    /// <summary>The change's timestamp, epoch seconds.</summary>
    public const string TimestampHeader = "x-azure-cosmos-ts";

    /// <summary>The document's entity tag after the change.</summary>
    public const string ETagHeader = "x-azure-cosmos-etag";

    /// <summary>The changes, in the order the feed delivered them.</summary>
    public IReadOnlyList<CosmosDbDocument> Documents { get; }

    public int Count => Documents.Count;

    public IExecutionRequest ForItem(int index) => ForDocument(Documents[index]);

    /// <summary>False, and not configurable: the extension reads no report from the function.</summary>
    public bool ReportsItemFailures => false;

    /// <summary>
    /// By checkpoint, because the feed is a log per partition, declared honestly although nothing
    /// reports: running the later changes after a failure would apply a newer version before the
    /// replay of an older one.
    /// </summary>
    public BatchFailureMode FailureMode => BatchFailureMode.Checkpoint;

    public IReadOnlyList<int> FailedItems => Array.Empty<int>();

    public void RecordFailure(int index, Exception failure) =>
        throw new NotSupportedException(
            "The extension reads no report from a change feed function, so an individual change " +
            "cannot be reported as failed. The invocation fails instead, which leaves the lease " +
            "where it was.");

    /// <summary>
    /// The request for one change, as a handler will see it.
    /// </summary>
    /// <remarks>
    /// <b>The body is the document, as it is now.</b> A handler binds the document that changed,
    /// system properties included - Cosmos puts them on the document rather than around it, and a
    /// handler that does not declare them does not see them.
    /// </remarks>
    public IExecutionRequest ForDocument(CosmosDbDocument document) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            ["Content-Type"] = "application/json"
        };

        Set(headers, IdHeader, document.Id);
        Set(headers, LsnHeader, document.Lsn);
        Set(headers, TimestampHeader, document.Timestamp);
        Set(headers, ETagHeader, document.ETag);

        return new FunctionsPayloadRequest(
            Method, Path, new MemoryStream(document.Json.ToArray(), writable: false), headers);
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
