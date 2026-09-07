using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;

namespace Hardened.Aws.Lambda.ApiGateway;

/// Case-insensitive rather than a camelCase policy, matching the other event contexts and AWS's
// own serializer. A policy only reaches properties whose names differ from the wire, and getting
// that judgement right per package is not worth the one line this saves.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
// The request only. The response is written field by field with a Utf8JsonWriter, because
// APIGatewayHttpApiV2ProxyResponse.Body is a string and binding one would copy a six-megabyte
// body through UTF-16 on its way back out to UTF-8.
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
internal partial class ApiGatewaySerializerContext : JsonSerializerContext {
}
