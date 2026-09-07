using System.Text;
using System.Text.Json;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Wraps plain JSON back into the type-tagged form DynamoDB puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The inverse of what the adapter does, and it exists so a test can write
/// <c>changes.Orders(new Order { Total = 42 })</c> and have the envelope that reaches the handler be
/// the one DynamoDB actually sends - <c>{"total":{"N":"42"}}</c> rather than <c>{"total":42}</c>.
/// Without it this delivery would hand the adapter a shape AWS never produces, and the unmarshalling
/// that makes a change feed handler readable would go untested at the only fidelity that tests it.
/// </para>
/// <para>
/// <b>An array becomes an L, never an SS or an NS.</b> Plain JSON cannot say "set", so a round trip
/// through here and back gives a list. That is the right way round: a list preserves order and
/// duplicates, so nothing a test wrote is lost, and a handler that genuinely needs set semantics is
/// reading <c>[NewImage]</c> rather than a bound parameter anyway.
/// </para>
/// </remarks>
internal static class AttributeValueWire {
    /// <summary>The item as DynamoDB would carry it, ready to drop into an image.</summary>
    public static string Item(string json) {
        using var document = JsonDocument.Parse(json);

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            WriteItem(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteItem(Utf8JsonWriter writer, JsonElement element) {
        writer.WriteStartObject();

        // A message that is not an object has no attributes to name. DynamoDB has no such item, so
        // this writes an empty image rather than inventing a key for it - and the handler binding
        // an empty object is a clearer failure than one binding a made-up shape.
        if (element.ValueKind == JsonValueKind.Object) {
            foreach (var property in element.EnumerateObject()) {
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.Value);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value) {
        writer.WriteStartObject();

        switch (value.ValueKind) {
            case JsonValueKind.String:
                writer.WriteString("S", value.GetString());

                break;

            case JsonValueKind.Number:
                // A string, because that is how DynamoDB carries every number - its range is wider
                // than any one binding type, so the wire form is the decimal text.
                writer.WriteString("N", value.GetRawText());

                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBoolean("BOOL", value.GetBoolean());

                break;

            case JsonValueKind.Null:
                writer.WriteBoolean("NULL", true);

                break;

            case JsonValueKind.Object:
                writer.WritePropertyName("M");
                WriteItem(writer, value);

                break;

            case JsonValueKind.Array:
                writer.WritePropertyName("L");
                writer.WriteStartArray();

                foreach (var item in value.EnumerateArray()) {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();

                break;

            default:
                writer.WriteBoolean("NULL", true);

                break;
        }

        writer.WriteEndObject();
    }
}
