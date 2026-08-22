using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
    private readonly ILogger<SystemTextJsonRequestDeserializer> _logger;

    /// <param name="resolvers">
    /// Every <c>IJsonTypeInfoResolver</c> the application registered, ahead of reflection. The
    /// response serializer takes the same set — a context that governs how an enum is written has
    /// to govern how it is read, or the application answers 400 to its own output.
    /// </param>
    public SystemTextJsonRequestDeserializer(IOptions<IJsonSerializerConfiguration> configuration,
        ILogger<SystemTextJsonRequestDeserializer> logger,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _logger = logger;
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

    public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        if (context.Request.Headers.TryGetValue("Content-Encoding", out var contentEncoding)) {
            return await DeserializeEncodedContent<T>(context, contentEncoding);
        }

        _logger.LogInformation($"Deserialize with option convert count {_serializerOptions.Converters.Count}");
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(context.Request.Body, _serializerOptions);
    }

    private async ValueTask<T?> DeserializeEncodedContent<T>(IExecutionContext context, StringValues contentEncoding) {
        if (contentEncoding.Contains(KnownEncoding.GZip)) {
            await using var decompressStream = new GZipStream(context.Request.Body, CompressionMode.Decompress);

            return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(decompressStream, _serializerOptions);
        }

        if (contentEncoding.Contains(KnownEncoding.Br)) {
            await using var decompressStream = new BrotliStream(context.Request.Body, CompressionMode.Decompress);

            return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(decompressStream, _serializerOptions);
        }

        throw new BadContentEncodingException(contentEncoding.ToString());
    }
}