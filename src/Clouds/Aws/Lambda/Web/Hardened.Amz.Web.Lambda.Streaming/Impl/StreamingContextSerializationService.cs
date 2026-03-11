using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

[SingletonService]
public class StreamingContextSerializationService : IContextSerializationService {
    private readonly ISerializationLocatorService _locatorService;
    private readonly IExceptionResponseSerializer _exceptionSerializer;
    private readonly INullValueResponseHandler _nullValueResponse;

    public StreamingContextSerializationService(
        ISerializationLocatorService locatorService,
        IExceptionResponseSerializer exceptionSerializer,
        INullValueResponseHandler nullValueResponse) {
        _locatorService = locatorService;
        _exceptionSerializer = exceptionSerializer;
        _nullValueResponse = nullValueResponse;
    }

    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        return _locatorService.FindRequestDeserializer(context).DeserializeRequestBody<T>(context);
    }

    public Task SerializeResponse(IExecutionContext context) {
        if (context.DefaultOutput != null) {
            return context.DefaultOutput(context);
        }

        if (context.Response.ExceptionValue != null) {
            return _exceptionSerializer.Handle(context, context.Response.ExceptionValue);
        }

        if (context.Response.ResponseValue == null) {
            return _nullValueResponse.Handle(context);
        }

        return _locatorService.FindResponseSerializer(context).SerializeResponse(context);
    }
}
