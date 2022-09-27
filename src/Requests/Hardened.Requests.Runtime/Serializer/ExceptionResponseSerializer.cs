using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer
{
    public class ExceptionResponseSerializer : IExceptionResponseSerializer
    {
        private readonly IRequestLogger _requestLogger;
        private readonly ISerializationLocatorService _serializationLocatorService;
        private readonly IExceptionToModelConverter _exceptionToModelConverter;

        public ExceptionResponseSerializer(
            IRequestLogger requestLogger, 
            ISerializationLocatorService serializationLocatorService, 
            IExceptionToModelConverter exceptionToModelConverter)
        {
            _requestLogger = requestLogger;
            _serializationLocatorService = serializationLocatorService;
            _exceptionToModelConverter = exceptionToModelConverter;
        }

        public Task Handle(IExecutionContext context, Exception exp)
        {
            var (status, model) = _exceptionToModelConverter.ConvertExceptionToModel(context, exp);

            context.Response.Status = status;
            context.Response.ResponseValue = model;

            return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
        }
    }
}
