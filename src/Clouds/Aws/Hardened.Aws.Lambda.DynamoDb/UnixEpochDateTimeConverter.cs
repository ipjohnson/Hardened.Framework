using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// Reads the epoch seconds DynamoDB sends into the <see cref="DateTime"/> the event model declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this the adapter throws on every real invocation.</b>
/// <c>StreamRecord.ApproximateCreationDateTime</c> is typed <c>DateTime</c> and
/// <c>Amazon.Lambda.DynamoDBEvents</c> carries no converter for it, so
/// <c>System.Text.Json</c> meets <c>"ApproximateCreationDateTime":1767225600</c> and reports that a
/// number is not a date. The package is written to be read through
/// <c>Amazon.Lambda.Serialization.SystemTextJson</c>, which installs a converter of its own; this
/// line does not take that package, so it brings its own.
/// </para>
/// <para>
/// A string is accepted as well as a number. Nothing AWS sends uses one, and a fixture written by
/// hand often does.
/// </para>
/// </remarks>
public sealed class UnixEpochDateTimeConverter : JsonConverter<DateTime> {
    public override DateTime Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Number) {
            // Seconds, and fractional on a stream that reports sub-second creation times. Read as a
            // double so the fraction survives to the DateTime rather than being truncated by a
            // long.
            return DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(reader.GetDouble() * 1000)).UtcDateTime;
        }

        return reader.GetDateTime();
    }

    /// <remarks>
    /// Written back as epoch seconds rather than ISO 8601, so a round trip through this converter
    /// produces what AWS sent. Nothing in the adapter serializes an event today; a test that builds
    /// one does.
    /// </remarks>
    public override void Write(
        Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(
            new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds());
}
