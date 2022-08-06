using System.IO.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class SystemTextJsonResponseSerializer : IResponseSerializer
    {
        public bool IsDefaultSerializer => true;

        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.Accept?.Contains("application/json") ?? false;
        }

        public async Task SerializeResponse(IExecutionContext context)
        {
            context.Response.ContentType = "application/json";

            if (context.Response.ResponseValue == null)
            {
                return;
            }

            if (context.Response.ShouldCompress)
            {
                await using var gzipStream = new GZipStream(context.Response.Body, CompressionLevel.Fastest, true);

                await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue);

                await gzipStream.FlushAsync();
            }
            else
            {
                await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue);
            }
        }
    }
}
