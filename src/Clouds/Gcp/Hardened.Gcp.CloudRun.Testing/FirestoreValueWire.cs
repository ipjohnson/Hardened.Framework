using System.Text.Json;
using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf.Collections;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// Wraps plain JSON back into the typed values a Firestore document carries.
/// </summary>
/// <remarks>
/// <para>
/// The inverse of what the adapter does, and it exists so a test can write
/// <c>changes.Orders(new Order { Total = 42 })</c> and have the event that reaches the handler be
/// the one Firestore actually sends - a <c>DocumentEventData</c> whose <c>total</c> is an
/// <c>integerValue</c> - rather than a shape the adapter would have produced itself. Without it the
/// value conversion that makes a change handler readable would go untested at the only fidelity
/// that tests it.
/// </para>
/// <para>
/// A JSON number with no fraction is an integer and one with a fraction is a double, which is the
/// distinction Firestore itself draws; a string is a string, never a timestamp or a reference,
/// because plain JSON cannot say which it meant.
/// </para>
/// </remarks>
internal static class FirestoreValueWire {
    public static void WriteFields(MapField<string, Value> fields, JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object) {
            return;
        }

        foreach (var property in element.EnumerateObject()) {
            fields[property.Name] = ToValue(property.Value);
        }
    }

    public static Value ToValue(JsonElement element) {
        switch (element.ValueKind) {
            case JsonValueKind.String:
                return new Value { StringValue = element.GetString() ?? "" };

            case JsonValueKind.Number:
                return element.TryGetInt64(out var integer)
                    ? new Value { IntegerValue = integer }
                    : new Value { DoubleValue = element.GetDouble() };

            case JsonValueKind.True:
            case JsonValueKind.False:
                return new Value { BooleanValue = element.GetBoolean() };

            case JsonValueKind.Object: {
                var map = new MapValue();

                WriteFields(map.Fields, element);

                return new Value { MapValue = map };
            }

            case JsonValueKind.Array: {
                var array = new ArrayValue();

                foreach (var item in element.EnumerateArray()) {
                    array.Values.Add(ToValue(item));
                }

                return new Value { ArrayValue = array };
            }

            default:
                return new Value { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue };
        }
    }
}
