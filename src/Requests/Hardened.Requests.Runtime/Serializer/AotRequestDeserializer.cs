using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Serializer;

public class AotRequestDeserializer : IRequestDeserializer {
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<AotRequestDeserializer> _logger;

    public AotRequestDeserializer(IOptions<IJsonSerializerConfiguration> configuration,
        ILogger<AotRequestDeserializer> logger,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _logger = logger;

        // Build options without a default reflection-based resolver so that
        // tests fail the same way AOT production does when source-gen type
        // registrations are missing.
        var sourceOptions = configuration.Value.DeSerializerOptions;
        _serializerOptions = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = sourceOptions?.PropertyNameCaseInsensitive ?? true,
            PropertyNamingPolicy = sourceOptions?.PropertyNamingPolicy ?? JsonNamingPolicy.CamelCase,
            NumberHandling = sourceOptions?.NumberHandling ?? JsonNumberHandling.AllowReadingFromString,
        };

        // Copy converters from configured options
        if (sourceOptions != null) {
            foreach (var converter in sourceOptions.Converters) {
                _serializerOptions.Converters.Add(converter);
            }
        }

        foreach (var resolver in resolvers) {
            _serializerOptions.TypeInfoResolverChain.Add(resolver);

            // Pull converters from source-generated contexts (e.g. UnixEpochDateTimeConverter)
            if (resolver is JsonSerializerContext ctx) {
                foreach (var converter in ctx.Options.Converters) {
                    if (!_serializerOptions.Converters.Contains(converter)) {
                        _serializerOptions.Converters.Add(converter);
                    }
                }
            }
        }
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
        return await System.Text.Json.JsonSerializer.DeserializeAsync(
            context.Request.Body, AotTypeInfo.For<T>(_serializerOptions));
    }

    private async ValueTask<T?> DeserializeEncodedContent<T>(IExecutionContext context, StringValues contentEncoding) {
        if (contentEncoding.Contains(KnownEncoding.GZip)) {
            await using var decompressStream = new GZipStream(context.Request.Body, CompressionMode.Decompress);

            return await System.Text.Json.JsonSerializer.DeserializeAsync(
                decompressStream, AotTypeInfo.For<T>(_serializerOptions));
        }

        if (contentEncoding.Contains(KnownEncoding.Br)) {
            await using var decompressStream = new BrotliStream(context.Request.Body, CompressionMode.Decompress);

            return await System.Text.Json.JsonSerializer.DeserializeAsync(
                decompressStream, AotTypeInfo.For<T>(_serializerOptions));
        }

        throw new BadContentEncodingException(contentEncoding.ToString());
    }
}
