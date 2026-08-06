using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

public class AotResponseSerializer : IResponseSerializer {
    private readonly JsonSerializerOptions _serializerOptions;

    public AotResponseSerializer(IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _serializerOptions =
            configuration.Value.SerializeOptions ??
            new(JsonSerializerDefaults.Web);

        foreach (var resolver in resolvers) {
            _serializerOptions.TypeInfoResolverChain.Add(resolver);
        }
    }

    public bool IsDefaultSerializer => true;

    public bool CanProcessContext(IExecutionContext context) {
        return context.Request.Accept?.Contains("application/json") ?? false;
    }

    public async Task SerializeResponse(IExecutionContext context) {
        context.Response.ContentType = "application/json";

        if (context.Response.ResponseValue == null) {
            return;
        }

        if (context.Response.ShouldCompress) {
            await using var gzipStream = new GZipStream(context.Response.Body, CompressionLevel.Fastest, true);

            // Serialize into the gzip stream, not the response body underneath it - writing
            // to the body directly leaves the payload uncompressed while a GZipStream is
            // open over it.
            await System.Text.Json.JsonSerializer.SerializeAsync(gzipStream, context.Response.ResponseValue,
                _serializerOptions);

            await gzipStream.FlushAsync();
        }
        else {
            await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue,
                _serializerOptions);
        }
    }
}
