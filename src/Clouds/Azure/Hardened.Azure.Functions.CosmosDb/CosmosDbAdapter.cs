using System.Text;
using System.Text.Json;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.CosmosDb;

/// <summary>
/// The Cosmos DB change feed, delivered as an ordered batch of the documents that changed.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a document and a route. A throw is rethrown, because failing
/// the invocation is what stops the extension advancing the lease past a change nothing handled.
/// </para>
/// <para>
/// <b>The shim binds the feed as a string, and this is why.</b> The host sends the batch as one
/// JSON array of documents. The extension can bind that to a collection of the handler's own
/// type, but the handler's type is the handler's business - one function serving one container
/// may have handlers that read different projections of it - so the adapter takes the array as
/// sent, splits it, and hands each document to the pipeline as a body the handler binds itself.
/// Nothing in this package touches the Cosmos SDK the extension brings.
/// </para>
/// <para>
/// <b>The current document only.</b> The change feed carries what a document is now, not what it
/// was: there is no old image on it, so <c>[OldImage]</c> on DynamoDB and the old value Firestore
/// carries have no counterpart here. That is a difference between the stores rather than a gap in
/// the adapter, and the DynamoDB adapter documents the other side of it.
/// </para>
/// <para>
/// Routes as <c>CHANGE /orders</c>, with the container's name from the shim: the host binds a
/// function to one container, so the function's identity is the route.
/// </para>
/// </remarks>
public sealed class CosmosDbAdapter : ITriggerAdapter {
    /// <summary>The scheme a change feed routes under, which <c>[Change]</c> declares.</summary>
    public const string ChangeScheme = "CHANGE";

    /// <summary>
    /// Whether the shim was generated for this family. A string on its own says nothing - three
    /// families bind one - so the scheme the shim carries is part of the check.
    /// </summary>
    public bool Handles(FunctionsTrigger trigger) =>
        trigger.Scheme == ChangeScheme && trigger.Data is string;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var documents = Split((string)trigger.Data);

        return new CosmosDbRequest(
            trigger.Scheme,
            trigger.Path,
            Stream.Null,
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            documents);
    }

    /// <summary>
    /// The documents in the batch, each as its own bytes, with the system properties a handler
    /// might want as headers read off it.
    /// </summary>
    /// <remarks>
    /// Each document's text is copied out of the feed as the host wrote it rather than kept as an
    /// element of the parsed document, because a <see cref="JsonElement"/> is a window into pooled
    /// memory and the forks outlive the parse. Copied, not re-serialized: a writer would escape
    /// the document differently from the feed, and the body is the document as it arrived.
    /// </remarks>
    internal static IReadOnlyList<CosmosDbDocument> Split(string feed) {
        var documents = new List<CosmosDbDocument>();

        if (string.IsNullOrWhiteSpace(feed)) {
            return documents;
        }

        using var parsed = JsonDocument.Parse(feed);

        if (parsed.RootElement.ValueKind != JsonValueKind.Array) {
            // One document rather than a batch, which the host does not send but a test might.
            documents.Add(Document(parsed.RootElement));

            return documents;
        }

        foreach (var element in parsed.RootElement.EnumerateArray()) {
            documents.Add(Document(element));
        }

        return documents;
    }

    private static CosmosDbDocument Document(JsonElement element) {
        return new CosmosDbDocument(
            Encoding.UTF8.GetBytes(element.GetRawText()),
            String(element, "id"),
            Number(element, "_lsn"),
            Number(element, "_ts"),
            String(element, "_etag"));
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <remarks>As its text, so a large sequence number is not narrowed on the way to a header.</remarks>
    private static string? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>
    /// Rethrown. Failing the invocation is what keeps the lease where it was, so the batch is
    /// delivered again - answering would advance it past a change nothing handled.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. The extension advances the lease on the invocation's outcome and reads no response.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);
}

/// <summary>
/// One changed document, as its own bytes, with the system properties Cosmos stamps on it.
/// </summary>
/// <param name="Json">The document as the feed carried it, system properties included.</param>
/// <param name="Id">The document's <c>id</c>, when it is a string.</param>
/// <param name="Lsn">
/// The change's log sequence number, <c>_lsn</c>, which is the position the feed is read by.
/// </param>
/// <param name="Timestamp">The change's <c>_ts</c>, epoch seconds.</param>
/// <param name="ETag">The document's <c>_etag</c> after the change.</param>
public sealed record CosmosDbDocument(
    ReadOnlyMemory<byte> Json,
    string? Id,
    string? Lsn,
    string? Timestamp,
    string? ETag);
