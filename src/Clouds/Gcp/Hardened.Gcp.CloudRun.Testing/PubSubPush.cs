using System.Text.Json;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// The body of a Pub/Sub push, as Pub/Sub writes it.
/// </summary>
/// <remarks>
/// Written field by field rather than serialized from a DTO, for the reason the Lambda envelopes
/// give: the wire form spells the message id and the publish time twice, <c>messageId</c> beside
/// <c>message_id</c>, which no naming policy produces, so a fixture built by round-tripping a type
/// would agree with the type and not with Pub/Sub.
/// </remarks>
public static class PubSubPush {
    /// <summary>
    /// One push, for <paramref name="subscription"/>, carrying <paramref name="data"/>.
    /// </summary>
    /// <param name="subscription">The full resource name, <c>projects/p/subscriptions/s</c>.</param>
    /// <param name="data">The message's bytes, which the push carries base64-encoded.</param>
    /// <param name="attributes">The message's attributes, or none.</param>
    /// <param name="messageId">The id Pub/Sub assigned, or none.</param>
    /// <param name="publishTime">When it was published, RFC 3339, or none.</param>
    /// <param name="orderingKey">The ordering key, or none.</param>
    /// <param name="deliveryAttempt">How many times Pub/Sub has tried, or none.</param>
    public static byte[] Body(
        string subscription,
        ReadOnlySpan<byte> data,
        IReadOnlyDictionary<string, string>? attributes = null,
        string? messageId = null,
        string? publishTime = null,
        string? orderingKey = null,
        int? deliveryAttempt = null) {
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteStartObject("message");

            writer.WriteBase64String("data", data);

            if (attributes is { Count: > 0 }) {
                writer.WriteStartObject("attributes");

                foreach (var attribute in attributes) {
                    writer.WriteString(attribute.Key, attribute.Value);
                }

                writer.WriteEndObject();
            }

            if (messageId != null) {
                writer.WriteString("messageId", messageId);
                writer.WriteString("message_id", messageId);
            }

            if (publishTime != null) {
                writer.WriteString("publishTime", publishTime);
                writer.WriteString("publish_time", publishTime);
            }

            if (orderingKey != null) {
                writer.WriteString("orderingKey", orderingKey);
            }

            writer.WriteEndObject();

            writer.WriteString("subscription", subscription);

            if (deliveryAttempt != null) {
                writer.WriteNumber("deliveryAttempt", deliveryAttempt.Value);
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
