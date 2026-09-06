using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;

namespace Hardened.Aws.Lambda.Runtime.Serialization;

/// <summary>
/// The AWS event types this package serialises, declared rather than reflected over.
/// </summary>
/// <remarks>
/// <para>
/// D3 says each adapter brings its own <c>JsonTypeInfo</c>, and this is the mechanism. It is not a
/// performance choice: it is what keeps ahead-of-time publishing auditable. An adapter that
/// deserialised reflectively would work on a JIT, warn under <c>PublishTrimmed</c>, and fail on a
/// field the trimmer removed - and the failure would land in production on whichever event shape
/// nothing exercised before the publish.
/// </para>
/// <para>
/// One context for the package rather than one per adapter, because a type declared twice is
/// generated twice. An adapter added later declares its event type here.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
public partial class LambdaEventSerializerContext : JsonSerializerContext {
}
