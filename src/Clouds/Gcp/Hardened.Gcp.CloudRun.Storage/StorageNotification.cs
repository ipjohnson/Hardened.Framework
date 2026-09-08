using System.Globalization;
using System.Text.Json;

namespace Hardened.Gcp.CloudRun.Storage;

/// <summary>
/// One object change, as both delivery forms describe it once the envelope is off.
/// </summary>
/// <param name="Bucket">The bucket the object is in.</param>
/// <param name="Name">The object's name, as the object is actually named.</param>
/// <param name="Size">The object's size in bytes, or null when the metadata did not carry one.</param>
/// <param name="ContentType">The object's content type, or null.</param>
/// <param name="Generation">The object's generation, or null. A string, because it is an int64 on the wire.</param>
/// <param name="ETag">The object's entity tag, or null.</param>
/// <param name="EventType">
/// <c>OBJECT_FINALIZE</c>, <c>OBJECT_DELETE</c>, <c>OBJECT_ARCHIVE</c> or
/// <c>OBJECT_METADATA_UPDATE</c>: the notification vocabulary, which the CloudEvent form's types
/// are mapped onto so both forms spell an event one way.
/// </param>
/// <param name="EventTime">When the change happened, as it was sent.</param>
/// <param name="TimeCreated">When the object was created, or null.</param>
/// <param name="Updated">When the object's metadata was last changed, or null.</param>
/// <remarks>
/// <b>The body is the notification, because there is no object in it.</b> Cloud Storage sends
/// metadata and leaves fetching the object to the handler, so the handler declares a type with
/// these properties the way it would declare one for a queue message. The projection mirrors the
/// S3 adapter's, with Storage's own words for the fields.
/// </remarks>
public sealed record StorageNotification(
    string Bucket,
    string Name,
    long? Size,
    string? ContentType,
    string? Generation,
    string? ETag,
    string EventType,
    string? EventTime,
    string? TimeCreated,
    string? Updated) {

    /// <summary>
    /// The notification from the object's metadata as Cloud Storage writes it, in the JSON API's
    /// form or the CloudEvent's, which agree on every field name.
    /// </summary>
    /// <remarks>
    /// <c>size</c> and <c>generation</c> are int64, which the JSON API and Eventarc's proto JSON
    /// both write as strings; the reader takes a number as well, because the published examples
    /// do.
    /// </remarks>
    public static StorageNotification From(
        JsonElement metadata, string eventType, string? eventTime, string? bucket = null, string? name = null) =>
        new(
            String(metadata, "bucket") ?? bucket ?? "",
            String(metadata, "name") ?? name ?? "",
            Int64(metadata, "size"),
            String(metadata, "contentType"),
            Text(metadata, "generation"),
            String(metadata, "etag"),
            eventType,
            eventTime,
            String(metadata, "timeCreated"),
            String(metadata, "updated"));

    /// <summary>The notification as the handler's body.</summary>
    public Stream Body() {
        var body = new MemoryStream();

        using (var writer = new Utf8JsonWriter(body)) {
            writer.WriteStartObject();
            writer.WriteString("bucket", Bucket);
            writer.WriteString("name", Name);

            if (Size is { } size) {
                writer.WriteNumber("size", size);
            }

            writer.WriteString("contentType", ContentType);
            writer.WriteString("generation", Generation);
            writer.WriteString("etag", ETag);
            writer.WriteString("eventType", EventType);
            writer.WriteString("eventTime", EventTime);
            writer.WriteString("timeCreated", TimeCreated);
            writer.WriteString("updated", Updated);
            writer.WriteEndObject();
        }

        body.Position = 0;

        return body;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A number or the text of one, as the wire form carries an int64 either way.</summary>
    private static string? Text(JsonElement element, string name) {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? Int64(JsonElement element, string name) {
        var text = Text(element, name);

        return text != null && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
