using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.ApplicationLoadBalancerEvents;

namespace Hardened.Aws.Lambda.Http;

// Payload format 1.0's types, in a context apart from 2.0's. Both AWS request types nest a class
// named ProxyRequestContext, and one context generates metadata for only one type of a given
// simple name, so sharing a context left 1.0's request context without any.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata
)]
[JsonSerializable(typeof(APIGatewayProxyRequest))]
[JsonSerializable(typeof(ApplicationLoadBalancerRequest))]
internal partial class LambdaProxySerializerContext : JsonSerializerContext;
