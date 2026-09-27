using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// Reads a <see cref="DateTimeOffset"/> from a request body only when the text states its offset.
/// </summary>
/// <remarks>
/// <para>
/// RFC 3339's date-time, which a contract's <c>format: date-time</c> names, requires one: <c>Z</c>
/// or <c>+hh:mm</c>. System.Text.Json also reads <c>2030-01-01T00:00:00</c>, as the server's local
/// time, so one request meant a different instant on servers in different time zones. A value
/// without an offset is refused like any other value the model cannot take.
/// </para>
/// <para>
/// Written as System.Text.Json writes it, so a response is unchanged.
/// </para>
/// </remarks>
public sealed class OffsetRequiredDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public static readonly OffsetRequiredDateTimeOffsetConverter Instance = new();

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // A JsonException with no message is filled in by the serializer, with the type and the
        // path, as its own converter's would be.
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var value))
        {
            throw new JsonException();
        }

        var text =
            reader.HasValueSequence || reader.ValueIsEscaped
                ? System.Text.Encoding.UTF8.GetBytes(reader.GetString()!)
                : reader.ValueSpan;

        if (!StatesOffset(text))
        {
            throw new JsonException(
                "The value is not an RFC 3339 date-time: it states no offset, such as Z or -05:00."
            );
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);

    /// <summary>
    /// Whether the time part ends in <c>Z</c> or carries a signed offset. A date with no time part
    /// states none.
    /// </summary>
    public static bool StatesOffset(ReadOnlySpan<byte> text)
    {
        var time = text.IndexOfAny((byte)'T', (byte)'t');

        if (time < 0)
        {
            return false;
        }

        var rest = text.Slice(time + 1);

        return rest.Length > 0
            && (rest[^1] is (byte)'Z' or (byte)'z' || rest.IndexOfAny((byte)'+', (byte)'-') >= 0);
    }
}
