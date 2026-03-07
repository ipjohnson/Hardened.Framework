using System.Text.Json.Serialization;
using Hardened.Amz.Function.Lambda.Streaming.Impl;

namespace Hardened.Amz.Function.Lambda.Streaming.Serializer;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LambdaErrorResponse))]
internal partial class StreamingEventSerializerContext : JsonSerializerContext;
