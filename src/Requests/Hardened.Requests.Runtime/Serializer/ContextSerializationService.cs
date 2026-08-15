using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class ContextSerializationService : IContextSerializationService {
    private readonly ILogger<ContextSerializationService> _logger;
    private readonly ISerializationLocatorService _serializationLocatorService;
    private readonly INullValueResponseHandler _nullValueResponse;
    private readonly IExceptionResponseSerializer _exceptionResponseSerializer;

    public ContextSerializationService(
        ILogger<ContextSerializationService> logger,
        ISerializationLocatorService serializationLocatorService,
        INullValueResponseHandler nullValueResponse,
        IExceptionResponseSerializer exceptionResponseSerializer) {
        _logger = logger;
        _serializationLocatorService = serializationLocatorService;
        _nullValueResponse = nullValueResponse;
        _exceptionResponseSerializer = exceptionResponseSerializer;
    }

    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        return _serializationLocatorService.FindRequestDeserializer(context).DeserializeRequestBody<T>(context);
    }

    public Task SerializeResponse(IExecutionContext context) {
        if (context.DefaultOutput != null) {
            return context.DefaultOutput(context);
        }

        // Before the output, deliberately. A handler that threw has no model to render, and handing
        // an exception to a view typed for something else would replace a legible error response
        // with a cast failure inside the render.
        if (context.Response.ExceptionValue != null) {
            return _exceptionResponseSerializer.Handle(context, context.Response.ExceptionValue);
        }

        var output = Output(context);

        if (output != null) {
            return output.SupportsContentType(context.Request.Accept, context)
                ? output.WriteOutput(context)
                : NotAcceptable(context);
        }

        if (context.Response.ResponseValue == null) {
            return _nullValueResponse.Handle(context);
        }

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }

    /// <summary>
    /// What writes this response, built once.
    /// </summary>
    private static IHardenedResponseOutput? Output(IExecutionContext context) {
        var response = context.Response;

        if (response.Output != null) {
            return response.Output;
        }

        var factory = response.OutputFactory;

        if (factory == null) {
            return null;
        }

        response.Output = factory(context);

        return response.Output;
    }

    /// <summary>
    /// The answer when a handler declares an output the client will not take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a fallback to JSON. A handler that declares an output has said what its response
    /// <em>is</em>, and a view usually renders a subset of what its model holds - a page showing a
    /// customer's name, from a model carrying their address and every internal identifier attached
    /// to them. Serializing that model because the client asked for
    /// <c>application/json</c> would put all of it on the wire, from a route whose author wrote
    /// nothing but a view.
    /// </para>
    /// <para>
    /// No body. 406 is the status; anything written would be the representation the client just
    /// said it could not read.
    /// </para>
    /// </remarks>
    private static Task NotAcceptable(IExecutionContext context) {
        context.Response.Status = 406;
        context.Response.ShouldSerialize = false;

        return Task.CompletedTask;
    }
}
