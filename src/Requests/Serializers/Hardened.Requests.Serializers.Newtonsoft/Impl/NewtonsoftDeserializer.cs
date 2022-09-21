using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl
{
    [Expose]
    public class NewtonsoftDeserializer : IRequestDeserializer
    {
        private readonly IMemoryStreamPool _memoryStreamPool;
        private readonly ISharedSerializer _sharedSerializer;
        private readonly ILogger<NewtonsoftDeserializer> _logger;
        public NewtonsoftDeserializer(IMemoryStreamPool memoryStreamPool, ISharedSerializer sharedSerializer, ILogger<NewtonsoftDeserializer> logger)
        {
            _memoryStreamPool = memoryStreamPool;
            _sharedSerializer = sharedSerializer;
            _logger = logger;
        }

        public bool IsDefaultSerializer => true;

        public bool CanProcessContext(IExecutionContext context)
        {
            return context.Request.ContentType?.Contains("application/json") ?? false;
        }


        public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
        {
            try
            {
                using var memoryStreamRes = _memoryStreamPool.Get();
                
                _logger.LogInformation("Length: " + memoryStreamRes.Item.Length);

                await context.Request.Body.CopyToAsync(_memoryStreamPool.Get().Item);

                _logger.LogInformation("Length: " + memoryStreamRes.Item.Length);

                _memoryStreamPool.Get().Item.Seek(0, SeekOrigin.Begin);

                using var textReader = new StreamReader(memoryStreamRes.Item,null, true, -1, true);
                using var jsonReader = new JsonTextReader(textReader);

                var stringValue = await textReader.ReadToEndAsync();

                _logger.LogInformation("Received: " + stringValue);

                memoryStreamRes.Item.Position = 0;

                return _sharedSerializer.Serializer.Deserialize<T>(jsonReader);
            }
            catch (Exception exp)
            {
                _logger.LogError(exp, "serializers threw exception " + exp.Message );
                throw;
            }
        }
    }
}
