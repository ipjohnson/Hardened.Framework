using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Web.Lambda.Streaming.Impl;

namespace Hardened.Amz.Web.Lambda.Streaming.Serializer;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(ResponsePrelude))]
[JsonSerializable(typeof(LambdaErrorResponse))]
internal partial class StreamingEventSerializerContext : JsonSerializerContext;
