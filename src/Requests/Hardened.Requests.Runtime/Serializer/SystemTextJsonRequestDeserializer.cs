using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// The reflection-based JSON deserializer, and the default when nothing replaces it.
/// </summary>
/// <remarks>
/// <para>
/// Annotated rather than fixed, because reflection is what this type is. It reads a model's shape
/// at run time, which is the behaviour an application wants until it publishes trimmed or AOT — at
/// which point the model it was reading may not be there any more.
/// </para>
/// <para>
/// The annotations put that on the record where it is used instead of inside the build.
/// <c>RequestRuntimeDI</c> registers this with <c>TryAdd</c>, and <c>AotSerializerModule</c>
/// registers the <c>Aot</c> deserializer over it — so an application that imports that module never
/// reaches this code, and one that does not gets told what it is relying on.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(Reason)]
[RequiresDynamicCode(Reason)]
public class SystemTextJsonRequestDeserializer : IRequestDeserializer {
    private const string Reason =
        "Reads the model's shape by reflection. Import AotSerializerModule for a trimmed or " +
        "AOT-published application, which registers the source-generated serializers instead.";

    private readonly JsonSerializerOptions _serializerOptions;

    /// <param name="resolvers">
    /// Every <c>IJsonTypeInfoResolver</c> the application registered, ahead of reflection. The
    /// response serializer takes the same set — a context that governs how an enum is written has
    /// to govern how it is read, or the application answers 400 to its own output.
    /// </param>
    public SystemTextJsonRequestDeserializer(IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _serializerOptions =
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.WithResolvers(
                configuration.Value.DeSerializerOptions ??
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                resolvers);
    }

    public bool IsDefaultSerializer => true;

    public bool CanProcessContext(IExecutionContext context) {
        return context.Request.ContentType?.Contains("application/json") ?? false;
    }

    /// <summary>
    /// Reads the body as it is. A compressed body was decoded by <c>RequestDecompressionFilter</c>
    /// before the bind, which is why this no longer looks at <c>Content-Encoding</c>.
    /// </summary>
    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) =>
        System.Text.Json.JsonSerializer.DeserializeAsync<T>(context.Request.Body, _serializerOptions);
}