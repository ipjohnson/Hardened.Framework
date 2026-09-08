using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened.CloudEvents;

/// <summary>
/// The structured JSON form, as System.Text.Json binds it.
/// </summary>
/// <remarks>
/// A binding type rather than <see cref="CloudEvent"/> itself, because the wire form and the
/// record disagree about what is required and how the payload is spelled: the wire has
/// <c>data</c> or <c>data_base64</c>, the record has bytes. Every attribute is nullable here so a
/// missing required one is reported by <see cref="CloudEventReader"/> by name rather than by a
/// deserializer failing to construct.
/// </remarks>
internal sealed class CloudEventDocument {
    [JsonPropertyName("specversion")]
    public string? SpecVersion { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; set; }

    [JsonPropertyName("dataschema")]
    public string? DataSchema { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("data_base64")]
    public string? DataBase64 { get; set; }

    /// <summary>Everything the specification does not name: the extension attributes.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>
/// The serializer context for the structured form, so reading one needs no reflection and
/// survives a trimmed or Native AOT publish.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CloudEventDocument))]
internal partial class CloudEventSerializerContext : JsonSerializerContext {
}
