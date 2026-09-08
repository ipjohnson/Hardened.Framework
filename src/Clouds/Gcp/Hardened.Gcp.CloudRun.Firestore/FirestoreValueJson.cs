using System.Text.Json;
using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf.Collections;

namespace Hardened.Gcp.CloudRun.Firestore;

/// <summary>
/// Writes a Firestore document out as the JSON a handler's own type is shaped like.
/// </summary>
/// <remarks>
/// <para>
/// A document event carries the document's fields as a map of typed values: <c>stringValue</c>,
/// <c>integerValue</c>, <c>mapValue</c> and the rest. A handler binds <c>order.Total</c> as a
/// number and <c>order.Sku</c> as a string, so something has to strip that form off. This is
/// that, and it is the reason a change feed handler reads like a queue handler - the same lift
/// the DynamoDB adapter does for attribute values.
/// </para>
/// <para>
/// An integer is written as an integer and a double as a double, so a value survives to whatever
/// the handler declared. A timestamp is written as RFC 3339 text, bytes as base64, a reference as
/// its path, and a geo point as an object with <c>latitude</c> and <c>longitude</c> - each the form
/// the handler's own type would bind.
/// </para>
/// </remarks>
public static class FirestoreValueJson {
    /// <summary>The document's fields as one JSON object; an empty object for no document.</summary>
    public static Stream Body(Document? document) {
        var body = new MemoryStream();

        using (var writer = new Utf8JsonWriter(body)) {
            WriteFields(writer, document?.Fields);
        }

        body.Position = 0;

        return body;
    }

    public static void WriteFields(Utf8JsonWriter writer, MapField<string, Value>? fields) {
        writer.WriteStartObject();

        if (fields != null) {
            foreach (var pair in fields) {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Value);
            }
        }

        writer.WriteEndObject();
    }

    public static void WriteValue(Utf8JsonWriter writer, Value? value) {
        if (value == null) {
            writer.WriteNullValue();

            return;
        }

        switch (value.ValueTypeCase) {
            case Value.ValueTypeOneofCase.StringValue:
                writer.WriteStringValue(value.StringValue);

                break;

            case Value.ValueTypeOneofCase.IntegerValue:
                writer.WriteNumberValue(value.IntegerValue);

                break;

            case Value.ValueTypeOneofCase.DoubleValue:
                writer.WriteNumberValue(value.DoubleValue);

                break;

            case Value.ValueTypeOneofCase.BooleanValue:
                writer.WriteBooleanValue(value.BooleanValue);

                break;

            case Value.ValueTypeOneofCase.TimestampValue:
                writer.WriteStringValue(value.TimestampValue.ToDateTimeOffset().ToString("O"));

                break;

            case Value.ValueTypeOneofCase.BytesValue:
                writer.WriteBase64StringValue(value.BytesValue.Span);

                break;

            case Value.ValueTypeOneofCase.ReferenceValue:
                writer.WriteStringValue(value.ReferenceValue);

                break;

            case Value.ValueTypeOneofCase.GeoPointValue:
                writer.WriteStartObject();
                writer.WriteNumber("latitude", value.GeoPointValue.Latitude);
                writer.WriteNumber("longitude", value.GeoPointValue.Longitude);
                writer.WriteEndObject();

                break;

            case Value.ValueTypeOneofCase.ArrayValue:
                writer.WriteStartArray();

                foreach (var item in value.ArrayValue.Values) {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();

                break;

            case Value.ValueTypeOneofCase.MapValue:
                WriteFields(writer, value.MapValue.Fields);

                break;

            default:
                // NullValue, and a value with nothing set, which Firestore does not produce.
                writer.WriteNullValue();

                break;
        }
    }
}
