using System.Text.Json.Serialization;
using Amazon.Lambda.SQSEvents;

namespace Hardened.Aws.Lambda.Sqs;

/// Case-insensitive, and no naming policy. The AWS event types carry no [JsonPropertyName] at all,
// and their wire names match neither convention: SQS sends "Records" against a property called
// Records, "messageId" against MessageId, and "eventSourceARN" against EventSourceArn - an acronym
// no policy produces. Case-insensitive matching is how AWS's own serializer binds them, and the
// only setting under which all three land. CamelCase here bound nothing, and an SQS batch arrived
// empty and routed to "/".
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SQSEvent))]
internal partial class SqsSerializerContext : JsonSerializerContext {
}
