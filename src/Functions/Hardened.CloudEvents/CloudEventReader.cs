using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Hardened.CloudEvents;

/// <summary>
/// Reads the two HTTP forms of a CloudEvents 1.0 event into one <see cref="CloudEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Structured mode is a body of <c>application/cloudevents+json</c> carrying every attribute and
/// the payload in one JSON object. Binary mode is a body that is the payload alone, with the
/// attributes in <c>ce-</c> headers and the payload's content type in <c>Content-Type</c>. Eventarc
/// delivers binary mode; Event Grid's CloudEvents schema and the Pub/Sub emulator's push through
/// Eventarc are structured. An adapter asks <see cref="IsStructured"/> and <see cref="IsBinary"/>
/// first, which cost a header lookup each, and only then reads.
/// </para>
/// <para>
/// Header values are taken as written. The specification percent-encodes a binary-mode value
/// outside printable ASCII; nothing this reader serves today sends one, so decoding is left until
/// an adapter needs it rather than applied to every header of every event.
/// </para>
/// </remarks>
public static class CloudEventReader {
    /// <summary>The media type of the structured form.</summary>
    public const string StructuredContentType = "application/cloudevents+json";

    /// <summary>What the binary form prefixes every context attribute header with.</summary>
    public const string HeaderPrefix = "ce-";

    private const string SpecVersion = "specversion";
    private const string Id = "id";
    private const string Source = "source";
    private const string Type = "type";
    private const string Subject = "subject";
    private const string Time = "time";
    private const string DataContentType = "datacontenttype";
    private const string DataSchema = "dataschema";
    private const string ContentType = "Content-Type";

    /// <summary>Whether <paramref name="contentType"/> announces the structured form, parameters allowed.</summary>
    public static bool IsStructured(string? contentType) =>
        contentType != null &&
        contentType.StartsWith(StructuredContentType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the headers carry the binary form: the required <c>ce-specversion</c> is the tell.</summary>
    public static bool IsBinary(IDictionary<string, StringValues> headers) =>
        Header(headers, HeaderPrefix + SpecVersion) != null;

    /// <summary>
    /// Reads whichever form the request is in, or throws when it is in neither.
    /// </summary>
    public static CloudEvent Read(
        string? contentType, IDictionary<string, StringValues> headers, ReadOnlyMemory<byte> body) {
        if (IsStructured(contentType)) {
            return ReadStructured(body.Span);
        }

        if (IsBinary(headers)) {
            return ReadBinary(headers, body);
        }

        throw new CloudEventFormatException(
            "The request is not a CloudEvent: its content type is not " + StructuredContentType +
            " and it carries no ce-specversion header.");
    }

    /// <summary>
    /// The structured form: one JSON object carrying the attributes and the payload.
    /// </summary>
    /// <exception cref="CloudEventFormatException">
    /// The body is not a JSON object, or one of <c>specversion</c>, <c>id</c>, <c>source</c> and
    /// <c>type</c> is missing.
    /// </exception>
    public static CloudEvent ReadStructured(ReadOnlySpan<byte> json) {
        CloudEventDocument? document;

        try {
            document = JsonSerializer.Deserialize(json, CloudEventSerializerContext.Default.CloudEventDocument);
        }
        catch (JsonException exception) {
            throw new CloudEventFormatException(
                "The structured CloudEvent body is not a JSON object.", exception);
        }

        if (document == null) {
            throw new CloudEventFormatException("The structured CloudEvent body is null.");
        }

        var extensions = new Dictionary<string, string>(StringComparer.Ordinal);

        if (document.Extensions != null) {
            foreach (var extension in document.Extensions) {
                extensions[extension.Key] = Text(extension.Value);
            }
        }

        return new CloudEvent(
            Required(document.SpecVersion, SpecVersion),
            Required(document.Id, Id),
            Required(document.Source, Source),
            Required(document.Type, Type)) {
            Subject = document.Subject,
            Time = document.Time,
            DataContentType = document.DataContentType,
            DataSchema = document.DataSchema,
            Data = StructuredData(document),
            Extensions = extensions
        };
    }

    /// <summary>
    /// The binary form: the payload as the body, the attributes as <c>ce-</c> headers.
    /// </summary>
    /// <remarks>
    /// <c>datacontenttype</c> is the request's own <c>Content-Type</c>, which is where the binary
    /// form puts it; a <c>ce-datacontenttype</c> header is not part of the form and is read as an
    /// extension if one is sent.
    /// </remarks>
    /// <exception cref="CloudEventFormatException">
    /// One of <c>ce-specversion</c>, <c>ce-id</c>, <c>ce-source</c> and <c>ce-type</c> is missing.
    /// </exception>
    public static CloudEvent ReadBinary(IDictionary<string, StringValues> headers, ReadOnlyMemory<byte> body) {
        var extensions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var header in headers) {
            if (!header.Key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var name = header.Key.Substring(HeaderPrefix.Length).ToLowerInvariant();

            if (name is SpecVersion or Id or Source or Type or Subject or Time or DataSchema) {
                continue;
            }

            extensions[name] = header.Value.ToString();
        }

        return new CloudEvent(
            Required(Header(headers, HeaderPrefix + SpecVersion), HeaderPrefix + SpecVersion),
            Required(Header(headers, HeaderPrefix + Id), HeaderPrefix + Id),
            Required(Header(headers, HeaderPrefix + Source), HeaderPrefix + Source),
            Required(Header(headers, HeaderPrefix + Type), HeaderPrefix + Type)) {
            Subject = Header(headers, HeaderPrefix + Subject),
            Time = Header(headers, HeaderPrefix + Time),
            DataContentType = Header(headers, ContentType),
            DataSchema = Header(headers, HeaderPrefix + DataSchema),
            Data = body,
            Extensions = extensions
        };
    }

    /// <summary>
    /// The payload of a structured event as bytes.
    /// </summary>
    /// <remarks>
    /// <c>data_base64</c> wins where both are present, because a producer that wrote it had bytes
    /// that were not JSON. A <c>data</c> that is a JSON string is the payload's text when the
    /// content type is not JSON, and a JSON document about a string when it is - the distinction
    /// the specification draws, and the one that lets <c>text/plain</c> travel without quotes.
    /// </remarks>
    private static ReadOnlyMemory<byte> StructuredData(CloudEventDocument document) {
        if (!string.IsNullOrEmpty(document.DataBase64)) {
            try {
                return Convert.FromBase64String(document.DataBase64!);
            }
            catch (FormatException exception) {
                throw new CloudEventFormatException("data_base64 is not base64.", exception);
            }
        }

        if (document.Data is not { } data || data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (data.ValueKind == JsonValueKind.String && !IsJson(document.DataContentType)) {
            return Encoding.UTF8.GetBytes(data.GetString() ?? "");
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer)) {
            data.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Whether a content type is JSON: absent, <c>application/json</c>, or a <c>+json</c> suffix,
    /// which is what the specification says <c>data</c> is JSON-encoded under.
    /// </summary>
    private static bool IsJson(string? contentType) {
        if (string.IsNullOrEmpty(contentType)) {
            return true;
        }

        var mediaType = contentType!;
        var semicolon = mediaType.IndexOf(';');

        if (semicolon > -1) {
            mediaType = mediaType.Substring(0, semicolon);
        }

        mediaType = mediaType.Trim();

        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static string Text(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();

    private static string Required(string? value, string attribute) =>
        string.IsNullOrEmpty(value)
            ? throw new CloudEventFormatException(
                "The CloudEvent is missing its required " + attribute + " attribute.")
            : value!;

    /// <summary>
    /// A header by name, whichever comparer the dictionary was built with.
    /// </summary>
    /// <remarks>
    /// A transport's own collection compares without regard to case and answers the first lookup;
    /// a plain dictionary a test built may not, and the scan is what keeps the two reading alike.
    /// </remarks>
    private static string? Header(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var direct)) {
            return direct.Count == 0 ? null : direct.ToString();
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value.Count == 0 ? null : header.Value.ToString();
            }
        }

        return null;
    }
}
