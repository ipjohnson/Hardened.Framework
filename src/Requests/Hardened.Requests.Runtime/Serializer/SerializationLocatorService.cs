using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class SerializationLocatorService : ISerializationLocatorService
    {
        private readonly IRequestDeserializer[] _requestDeserializers;
        private readonly IResponseSerializer[] _responseSerializers;

        public SerializationLocatorService(
            IEnumerable<IRequestDeserializer> requestDeserializers,
            IEnumerable<IResponseSerializer> responseSerializers)
        {
            // reverse lists so user registered serializers are tested first
            _requestDeserializers = requestDeserializers.Reverse().ToArray();
            _responseSerializers = responseSerializers.Reverse().ToArray();
        }

        public IRequestDeserializer FindRequestDeserializer(IExecutionContext context)
        {
            IRequestDeserializer? defaultSerializer = null;

            for (var i = 0; i < _requestDeserializers.Length; i++)
            {
                var requestDeserializer = _requestDeserializers[i];

                if (requestDeserializer.CanProcessContext(context))
                {
                    return requestDeserializer;
                }

                if (requestDeserializer.IsDefaultSerializer)
                {
                    defaultSerializer ??= requestDeserializer;
                }
            }

            if (defaultSerializer != null)
            {
                return defaultSerializer;
            }

            throw new Exception("Could not find serializer: " + context.Request.ContentType);
        }

        public IResponseSerializer FindResponseSerializer(IExecutionContext context)
        {
            IResponseSerializer? defaultSerializer = null;

            for (var i = 0; i < _responseSerializers.Length; i++)
            {
                var responseSerializer = _responseSerializers[i];

                if (responseSerializer.CanProcessContext(context))
                {
                    return responseSerializer;
                }

                if (responseSerializer.IsDefaultSerializer)
                {
                    defaultSerializer = responseSerializer;
                }
            }

            if (defaultSerializer != null)
            {
                return defaultSerializer;
            }

            throw new Exception("Could not locate response serialize: " + context.Request.ContentType);
        }
    }
}
