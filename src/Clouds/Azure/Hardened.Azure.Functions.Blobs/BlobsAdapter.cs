using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Blobs;

/// <summary>
/// Blob Storage, one blob per invocation, as the notification the host raised for it.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped and never batched: the trigger fires once per blob. A throw is rethrown, so the
/// host retries the blob and, in the end, records it as poison rather than as handled.
/// </para>
/// <para>
/// <b>The body is the notification, not the blob.</b> Every other adapter hands a handler
/// something a publisher wrote; Storage sends a blob's name and the handler decides whether to
/// fetch it. So the body is the same projection the S3 adapter binds - the container, the name,
/// the size and the event - and a handler declares a type with those properties.
/// </para>
/// <para>
/// <b>The shim binds a <c>BlobClient</c>, and this is why.</b> The extension binds a blob trigger
/// to a string, a stream or bytes by downloading the blob, which is exactly what a notification
/// must not cost. A <c>BlobClient</c> is bound without a request: it carries the container and
/// the name, and the size comes from the trigger metadata the host sends beside it. A handler
/// that wants the content fetches it, which is the one way a blob trigger differs from every
/// other trigger in the framework.
/// </para>
/// <para>
/// Fed by Event Grid rather than by scanning the container, which is the source the generated
/// binding names: scanning is the extension's fallback for accounts without events and can take
/// minutes to notice a blob.
/// </para>
/// </remarks>
public sealed class BlobsAdapter : ITriggerAdapter {
    /// <summary>The scheme a blob container routes under, which <c>[Blob]</c> declares.</summary>
    public const string BlobScheme = "BLOB";

    /// <summary>The blob's container.</summary>
    public const string ContainerHeader = "x-azure-blob-container";

    /// <summary>The blob's name within the container.</summary>
    public const string NameHeader = "x-azure-blob-name";

    /// <summary>The blob's size in bytes, when the host said.</summary>
    public const string SizeHeader = "x-azure-blob-size";

    /// <summary>The blob's own URI.</summary>
    public const string UriHeader = "x-azure-blob-uri";

    /// <summary>
    /// What the blob trigger fires for. Storage raises the trigger for a created or updated blob
    /// and never for a deletion, so the event is the one thing here the notification could not
    /// have said otherwise.
    /// </summary>
    public const string BlobCreated = "BlobCreated";

    /// <summary>Whether the shim was generated for this family, which is a type check.</summary>
    public bool Handles(FunctionsTrigger trigger) => trigger.Data is BlobClient;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var blob = (BlobClient)trigger.Data;
        var properties = Properties(context);

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            ["Content-Type"] = "application/json",
            [ContainerHeader] = blob.BlobContainerName,
            [NameHeader] = blob.Name,
            [UriHeader] = blob.Uri.ToString()
        };

        if (properties.Size is { } size) {
            headers[SizeHeader] = size.ToString(CultureInfo.InvariantCulture);
        }

        var body = new MemoryStream();

        using (var writer = new Utf8JsonWriter(body)) {
            writer.WriteStartObject();
            writer.WriteString("container", blob.BlobContainerName);
            writer.WriteString("name", blob.Name);

            if (properties.Size is { } written) {
                writer.WriteNumber("size", written);
            }

            writer.WriteString("contentType", properties.ContentType);
            writer.WriteString("eTag", properties.ETag);
            writer.WriteString("uri", blob.Uri.ToString());
            writer.WriteString("eventName", BlobCreated);
            writer.WriteEndObject();
        }

        body.Position = 0;

        return new FunctionsPayloadRequest(trigger.Scheme, trigger.Path, body, headers);
    }

    /// <summary>
    /// The blob's properties as the host sends them beside the trigger, read for the facts the
    /// client does not carry without a request.
    /// </summary>
    /// <remarks>
    /// The host serializes the blob's properties to JSON under <c>Properties</c> in the trigger
    /// metadata. The names are the WebJobs SDK's - <c>Length</c> for the size, and
    /// <c>ContentLength</c> on older hosts - and both are read.
    /// </remarks>
    internal static BlobProperties Properties(FunctionContext context) {
        if (!context.BindingContext.BindingData.TryGetValue("Properties", out var raw) || raw is not string json) {
            return BlobProperties.Unknown;
        }

        try {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return BlobProperties.Unknown;
            }

            long? size = null;
            string? contentType = null;
            string? eTag = null;

            foreach (var property in document.RootElement.EnumerateObject()) {
                if (property.Name is "Length" or "ContentLength" && property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt64(out var length)) {
                    size ??= length;
                }
                else if (property.Name == "ContentType" && property.Value.ValueKind == JsonValueKind.String) {
                    contentType = property.Value.GetString();
                }
                else if (property.Name == "ETag" && property.Value.ValueKind == JsonValueKind.String) {
                    eTag = property.Value.GetString();
                }
            }

            return new BlobProperties(size, contentType, eTag);
        }
        catch (JsonException) {
            return BlobProperties.Unknown;
        }
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>Rethrown. Failing the invocation is what makes the host retry the blob.</summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. A blob trigger reads no response.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);

    /// <summary>What the host said about the blob, or nothing.</summary>
    public sealed record BlobProperties(long? Size, string? ContentType, string? ETag) {
        public static readonly BlobProperties Unknown = new(null, null, null);
    }
}
