using System.Text.Json.Serialization;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(StructuredTupleData))]
internal partial class RecordSerializer : JsonSerializerContext
{
        
}
