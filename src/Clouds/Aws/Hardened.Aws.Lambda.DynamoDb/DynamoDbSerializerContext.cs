using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon.Lambda.DynamoDBEvents;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// The DynamoDB stream event, source-generated.
/// </summary>
/// <remarks>
/// <para>
/// Its own context rather than one shared across the AWS line, which is what keeps the module a
/// trimming boundary: a shared context would root every event type in the package and leave the
/// modules deciding nothing.
/// </para>
/// <para>
/// <b>Case-insensitive rather than a naming policy</b>, because the event has no single convention
/// to name. <c>Amazon.Lambda.DynamoDBEvents</c> carries no <c>JsonPropertyName</c> on anything and
/// the payload mixes cases in one document - <c>eventSourceARN</c> and <c>eventName</c> beside
/// <c>NewImage</c> and <c>SequenceNumber</c> - so camelCase binds the outer record and silently
/// leaves every image null.
/// </para>
/// </remarks>
[JsonSerializable(typeof(DynamoDBEvent))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class DynamoDbSerializerContext : JsonSerializerContext { }

/// <summary>
/// The event's metadata, resolved once, with the epoch converter installed.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate class, because these cannot be static fields on the context.</b> The generated
/// <c>Default</c> is itself a static field on <see cref="DynamoDbSerializerContext"/>, and a field
/// initialiser there that reads it runs inside the same static constructor - so the resolver is
/// still null and the first deserialize fails with "metadata was not provided by TypeInfoResolver
/// of type '&lt;null&gt;'". Here the context is fully initialised before anything reads it.
/// </para>
/// <para>
/// <b>Resolved to a <c>JsonTypeInfo</c> rather than left as options</b>, because the overload
/// taking options is annotated RequiresUnreferencedCode and RequiresDynamicCode - it has to assume
/// a reflection resolver could be installed. Passing the metadata is the trim-safe overload, which
/// matters here more than most: this package sets IsAotCompatible, and the argument for a module
/// per adapter is that an unapplied one gets removed.
/// </para>
/// </remarks>
public static class DynamoDbEventJson {
    private static readonly JsonSerializerOptions Options = new() {
        TypeInfoResolver = DynamoDbSerializerContext.Default,
        PropertyNameCaseInsensitive = true,
        Converters = { new UnixEpochDateTimeConverter() }
    };

    /// <summary>The stream event, ready to deserialize.</summary>
    public static readonly JsonTypeInfo<DynamoDBEvent> Event =
        (JsonTypeInfo<DynamoDBEvent>)Options.GetTypeInfo(typeof(DynamoDBEvent));
}
