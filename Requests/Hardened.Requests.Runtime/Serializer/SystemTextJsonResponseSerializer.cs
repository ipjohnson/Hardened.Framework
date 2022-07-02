using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class SystemTextJsonResponseSerializer : IResponseSerializer
    {
        public bool IsDefaultSerializer => true;

        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.Accepts?.Contains("application/json") ?? false;
        }

        public Task SerializeResponse(IExecutionContext context)
        {
            if (context.Response.ResponseValue == null)
            {
                return Task.CompletedTask;
            }

            context.Response.ContentType = "application/json";

            return System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body,
                context.Response.ResponseValue);
        }
    }
}
