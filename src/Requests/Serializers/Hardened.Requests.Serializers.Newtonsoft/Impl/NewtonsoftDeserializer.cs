using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl
{
    [Expose]
    public class NewtonsoftDeserializer : IRequestDeserializer
    {
        private readonly MemoryStreamPool _memoryStreamPool;
        private readonly ISharedSerializer _sharedSerializer;

        public NewtonsoftDeserializer(MemoryStreamPool memoryStreamPool, ISharedSerializer sharedSerializer)
        {
            _memoryStreamPool = memoryStreamPool;
            _sharedSerializer = sharedSerializer;
        }

        public bool IsDefaultSerializer => true;

        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.ContentType?.Contains("application/json") ?? false;
        }


        public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
        {
            using var memoryStreamRes = _memoryStreamPool.Get();

            await context.Request.Body.CopyToAsync(_memoryStreamPool.Get().Item);

            memoryStreamRes.Item.Position = 0;

            using var textReader = new StreamReader(memoryStreamRes.Item);
            using var jsonReader = new JsonTextReader(textReader);

            return _sharedSerializer.Serializer.Deserialize<T>(jsonReader);
        }
    }
}
