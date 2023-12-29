using System.IO.Compression;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

public class SystemTextJsonResponseSerializer : IResponseSerializer {
    private readonly JsonSerializerOptions _serializerOptions;

    public SystemTextJsonResponseSerializer(IOptions<IJsonSerializerConfiguration> configuration) {
        _serializerOptions =
            configuration.Value.DeSerializerOptions ??
            new(JsonSerializerDefaults.Web);
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

            await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue,
                _serializerOptions);

            await gzipStream.FlushAsync();
        }
        else {
            await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue,
                _serializerOptions);
        }
    }
}