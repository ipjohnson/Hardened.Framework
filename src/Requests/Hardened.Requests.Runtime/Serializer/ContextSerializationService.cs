using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class ContextSerializationService : IContextSerializationService {
    private readonly ILogger<ContextSerializationService> _logger;
    private readonly ISerializationLocatorService _serializationLocatorService;
    private readonly INullValueResponseHandler _nullValueResponse;
    private readonly IExceptionResponseSerializer _exceptionResponseSerializer;
    private readonly ITemplateResponseSerializer _templateResponseSerializer;

    public ContextSerializationService(
        ILogger<ContextSerializationService> logger,
        ISerializationLocatorService serializationLocatorService,
        INullValueResponseHandler nullValueResponse,
        IExceptionResponseSerializer exceptionResponseSerializer,
        ITemplateResponseSerializer templateResponseSerializer) {
        _logger = logger;
        _serializationLocatorService = serializationLocatorService;
        _nullValueResponse = nullValueResponse;
        _exceptionResponseSerializer = exceptionResponseSerializer;
        _templateResponseSerializer = templateResponseSerializer;
    }

    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        return _serializationLocatorService.FindRequestDeserializer(context).DeserializeRequestBody<T>(context);
    }

    public Task SerializeResponse(IExecutionContext context) {
        if (context.DefaultOutput != null) {
            return context.DefaultOutput(context);
        }

        if (context.Response.ExceptionValue != null) {
            return _exceptionResponseSerializer.Handle(context, context.Response.ExceptionValue);
        }

        if (context.Response.ResponseValue == null) {
            return _nullValueResponse.Handle(context);
        }

        // Ahead of the locator, and deliberately not as one more IResponseSerializer among the
        // others. The locator returns the first registered serializer that claims the context, so a
        // template response whose request also says Accept: application/json - which is every
        // browser request, and the default in the test host - resolves on registration order. Both
        // candidates are registered by this assembly's own module, so there is no ordering an
        // application could choose to make the template win. Asking here is the only way the answer
        // does not depend on the order two services happened to be registered in.
        if (_templateResponseSerializer.CanProcessContext(context)) {
            return _templateResponseSerializer.SerializeResponse(context);
        }

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }
}