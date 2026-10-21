using System.Text.Json;
using System.Text.Json.Nodes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Sample;

/// <summary>
/// Copied into a code-first Refit scaffold by scripts/verify-templates.sh, so the client Refitter
/// generates from the document has to compile over the System.Text.Json DOM types. Their schemas
/// once carried a property named this[], which Refitter refused.
/// </summary>
public record JsonDomEnvelope(
    JsonElement Element,
    JsonNode Node,
    JsonObject Object,
    JsonArray Array,
    JsonValue Value,
    JsonObject? MaybeObject
);

public class JsonDomController
{
    [Post("/dom/envelope")]
    public JsonDomEnvelope Envelope(JsonDomEnvelope body) => body;

    [Post("/dom/element")]
    public JsonElement Element([FromBody] JsonElement body) => body;

    [Post("/dom/node")]
    public JsonNode Node([FromBody] JsonNode body) => body;

    [Post("/dom/object")]
    public JsonObject Object([FromBody] JsonObject body) => body;

    [Post("/dom/array")]
    public JsonArray Array([FromBody] JsonArray body) => body;

    [Post("/dom/value")]
    public JsonValue Value([FromBody] JsonValue body) => body;
}
