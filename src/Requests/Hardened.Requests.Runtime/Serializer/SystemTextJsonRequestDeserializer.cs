using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Serializer
{
    public class SystemTextJsonRequestDeserializer : IRequestDeserializer
    {
        private readonly JsonSerializerOptions _serializerOptions;

        public SystemTextJsonRequestDeserializer(IOptions<IJsonSerializerConfiguration> configuration)
        {
            _serializerOptions = 
                configuration.Value.DeSerializerOptions ?? 
                new(JsonSerializerDefaults.Web);
        }

        public bool IsDefaultSerializer => true;

        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.ContentType?.Contains("application/json") ?? false;
        }

        public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
        {
            if (context.Request.Headers.TryGet("Content-Encoding", out var contentEncoding))
            {
                return await DeserializeEncodedContent<T>(context, contentEncoding);
            }

            return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(context.Request.Body, _serializerOptions);
        }

        private async ValueTask<T?> DeserializeEncodedContent<T>(IExecutionContext context, StringValues contentEncoding)
        {
            if (contentEncoding.Contains("gzip"))
            {
                await using var decompressStream = new GZipStream(context.Request.Body, CompressionMode.Decompress);

                return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(decompressStream);
            }
            
            if (contentEncoding.Contains("br"))
            {
                await using var decompressStream = new BrotliStream(context.Request.Body, CompressionMode.Decompress);

                return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(decompressStream);
            }
            
            throw new BadContentEncodingException(contentEncoding);
        }
    }
}
