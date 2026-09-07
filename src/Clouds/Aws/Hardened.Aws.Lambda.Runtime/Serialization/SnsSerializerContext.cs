using System.Text.Json.Serialization;
using Amazon.Lambda.SNSEvents;

namespace Hardened.Aws.Lambda.Runtime.Serialization;

/// Case-insensitive, and no naming policy, as every AWS event context here is. SNS makes the
// reason visible from the other side: its wire shape is PascalCase throughout - "Records",
// "EventSource", "Sns", "TopicArn" - where SQS's is camelCase with an acronym in it. One policy
// cannot serve both, and case-insensitive matching serves both without one.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SNSEvent))]
internal partial class SnsSerializerContext : JsonSerializerContext {
}
