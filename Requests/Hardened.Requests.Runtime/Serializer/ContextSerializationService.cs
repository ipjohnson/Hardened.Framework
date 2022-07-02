using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class ContextSerializationService : IContextSerializationService
    {
        private readonly IRequestDeserializer[] _requestDeserializers;
        private readonly IResponseSerializer[] _responseSerializers;

        public ContextSerializationService(IEnumerable<IRequestDeserializer> requestDeserializers, IEnumerable<IResponseSerializer> responseSerializers)
        {
            _requestDeserializers = requestDeserializers.Reverse().ToArray();
            _responseSerializers = responseSerializers.Reverse().ToArray();
        }

        public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
        {
            for (var i = 0; i < _requestDeserializers.Length; i++)
            {
                var requestDeserializer = _requestDeserializers[i];

                if (requestDeserializer.CanProcessContext(context))
                {
                    return requestDeserializer.DeserializeRequestBody<T>(context);
                }
            }

            // TODO: add special handler for when no serializer found
            throw new Exception("Could not find serializer: " + context.Request.ContentType);
        }

        public Task SerializeResponse(IExecutionContext context)
        {
            if (context.DefaultOutput != null)
            {
                return context.DefaultOutput(context);
            }

            IResponseSerializer? defaultSerializer = null;

            for (var i = 0; i < _responseSerializers.Length; i++)
            {
                var responseSerializer = _responseSerializers[i];

                if (responseSerializer.CanProcessContext(context))
                {
                    return responseSerializer.SerializeResponse(context);
                }
                
                if (responseSerializer.IsDefaultSerializer)
                {
                    defaultSerializer = responseSerializer;
                }
            }

            if (defaultSerializer != null)
            {
                return defaultSerializer.SerializeResponse(context);
            }

            throw new Exception("Could not locate response serialize: " + context.Request.ContentType);
        }
    }
}
