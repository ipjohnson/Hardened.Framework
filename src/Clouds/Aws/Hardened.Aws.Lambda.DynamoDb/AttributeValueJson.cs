using System.Text.Json;
using Amazon.Lambda.DynamoDBEvents;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// Writes a DynamoDB item out as the JSON a handler's own type is shaped like.
/// </summary>
/// <remarks>
/// <para>
/// A stream record carries an item in DynamoDB's wire form, where every value is a one-key object
/// naming its type: <c>{"total":{"N":"42"},"sku":{"S":"abc"}}</c>. A handler binds
/// <c>record.Total</c> as a number and <c>record.Sku</c> as a string, so something has to strip that
/// form off. This is that, and it is the reason a change feed handler reads like a queue handler.
/// </para>
/// <para>
/// <b>Written rather than converted through the SDK.</b> <c>Amazon.Lambda.DynamoDBEvents</c> has no
/// dependencies and is 32 KB; the document model that unmarshals for you lives in
/// <c>AWSSDK.DynamoDBv2</c>, which is two orders of magnitude larger and would land in the bundle
/// of every function that handles one table.
/// </para>
/// <para>
/// <b>A number is written as a number, not as a string.</b> DynamoDB stores every number as a
/// decimal string because its range is wider than any one binding type, and passing that through
/// would make a handler declare <c>string Total</c>. What is written back is the literal text
/// DynamoDB stored, so a value too large for <c>double</c> survives to whatever the handler
/// declared - <c>System.Text.Json</c> reads a raw number into <c>decimal</c>, <c>long</c> or
/// <c>double</c> as the target asks.
/// </para>
/// </remarks>
internal static class AttributeValueJson {
    public static void WriteItem(
        Utf8JsonWriter writer, IDictionary<string, DynamoDBEvent.AttributeValue>? item) {
        writer.WriteStartObject();

        if (item != null) {
            foreach (var pair in item) {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Value);
            }
        }

        writer.WriteEndObject();
    }

    /// <remarks>
    /// The order of these tests is the order of the shapes' likelihood, and nothing rests on it -
    /// exactly one field of an DynamoDBEvent.AttributeValue is ever set.
    /// </remarks>
    private static void WriteValue(Utf8JsonWriter writer, DynamoDBEvent.AttributeValue? value) {
        if (value == null) {
            writer.WriteNullValue();

            return;
        }

        if (value.S != null) {
            writer.WriteStringValue(value.S);
        }
        else if (value.N != null) {
            // Raw, so the decimal text DynamoDB stored is the number the binder reads. Writing it
            // through a double first would round a 38-digit value on the way past.
            writer.WriteRawValue(value.N);
        }
        else if (value.BOOL is { } boolean) {
            writer.WriteBooleanValue(boolean);
        }
        else if (value.NULL is true) {
            writer.WriteNullValue();
        }
        else if (value.M != null) {
            WriteItem(writer, value.M);
        }
        else if (value.L != null) {
            writer.WriteStartArray();

            foreach (var item in value.L) {
                WriteValue(writer, item);
            }

            writer.WriteEndArray();
        }
        else if (value.SS != null) {
            WriteArray(writer, value.SS, (w, item) => w.WriteStringValue(item));
        }
        else if (value.NS != null) {
            WriteArray(writer, value.NS, (w, item) => w.WriteRawValue(item));
        }
        else if (value.B != null) {
            // Base64, which is what a byte[] binds from and what the wire form already held. The
            // package models a binary attribute as a MemoryStream rather than bytes.
            writer.WriteBase64StringValue(value.B.ToArray());
        }
        else if (value.BS != null) {
            WriteArray(writer, value.BS, (w, item) => w.WriteBase64StringValue(item.ToArray()));
        }
        else {
            // An DynamoDBEvent.AttributeValue with nothing set. DynamoDB does not produce one, and guessing a
            // value for it would be worse than a null the handler can see.
            writer.WriteNullValue();
        }
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer, IEnumerable<T> items, Action<Utf8JsonWriter, T> write) {
        writer.WriteStartArray();

        foreach (var item in items) {
            write(writer, item);
        }

        writer.WriteEndArray();
    }
}
