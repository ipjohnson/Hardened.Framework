using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Serializer;

public class ContextSerializationService : IContextSerializationService
{
    private readonly ILogger<ContextSerializationService> _logger;
    private readonly ISerializationLocatorService _serializationLocatorService;
    private readonly INullValueResponseHandler _nullValueResponse;
    private readonly IExceptionResponseSerializer _exceptionResponseSerializer;

    public ContextSerializationService(
        ILogger<ContextSerializationService> logger,
        ISerializationLocatorService serializationLocatorService, 
        INullValueResponseHandler nullValueResponse, 
        IExceptionResponseSerializer exceptionResponseSerializer)
    {
        _logger = logger;
        _serializationLocatorService = serializationLocatorService;
        _nullValueResponse = nullValueResponse;
        _exceptionResponseSerializer = exceptionResponseSerializer;
    }

    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context)
    {
        return _serializationLocatorService.FindRequestDeserializer(context).DeserializeRequestBody<T>(context);
    }

    public Task SerializeResponse(IExecutionContext context)
    {
        if (context.DefaultOutput != null)
        {
            return context.DefaultOutput(context);
        }

        if (context.Response.ExceptionValue != null)
        {
            return _exceptionResponseSerializer.Handle(context, context.Response.ExceptionValue);
        }

        if (context.Response.ResponseValue == null)
        {
            return _nullValueResponse.Handle(context);
        }

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }
}