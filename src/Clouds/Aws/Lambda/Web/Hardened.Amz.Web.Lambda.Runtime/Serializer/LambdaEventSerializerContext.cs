using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;

namespace Hardened.Amz.Web.Lambda.Runtime.Serializer;

/// <summary>
/// The payload format 2.0 event and its response, read from and written to the bootstrap's raw
/// streams. Source-generated so the host is trimmable and needs no reflection at the wire.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
internal partial class LambdaEventSerializerContext : JsonSerializerContext;
