using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class SystemTextJsonRequestDeserializer : IRequestDeserializer
    {
        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.ContentType.Contains("application/json");
        }

        public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
        {
            return await System.Text.Json.JsonSerializer.DeserializeAsync<T?>(context.Request.Body);
        }
    }
}
