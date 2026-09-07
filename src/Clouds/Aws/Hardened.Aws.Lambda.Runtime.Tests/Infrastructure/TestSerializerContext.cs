using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SNSEvents;
using Amazon.Lambda.SQSEvents;

namespace Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// The AWS types the tests read back, which the package itself does not serialise.
/// </summary>
/// <remarks>
/// <c>APIGatewayHttpApiV2ProxyResponse</c> is here rather than in the shipped context because
/// nothing in the package binds it - the adapter writes the response field by field. Deserializing
/// the written bytes into AWS's own type is the assertion: it proves the hand-written payload is
/// exactly the shape the type expects, which is a stronger check than comparing against a string
/// the test wrote itself.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(SQSEvent))]
[JsonSerializable(typeof(SNSEvent))]
// The dictionary value types, named explicitly, and renamed. Writing an event is the fast path,
// which needs metadata for a dictionary's value type that walking the root does not produce. Both
// are called MessageAttribute, and a context names its generated property after the simple type
// name - so declaring both silently produced one property and left the other unresolvable at run
// time. The shipped contexts never meet either problem: they are metadata-only, never serialise,
// and hold one event each.
[JsonSerializable(typeof(SQSEvent.MessageAttribute), TypeInfoPropertyName = "SqsMessageAttribute")]
[JsonSerializable(typeof(SNSEvent.MessageAttribute), TypeInfoPropertyName = "SnsMessageAttribute")]
internal partial class TestSerializerContext : JsonSerializerContext {
}
