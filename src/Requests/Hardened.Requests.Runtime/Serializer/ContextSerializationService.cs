using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
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
        try {
            return SerializeAcceptedResponse(context);
        }
        catch (NotAcceptableException notAcceptable) {
            // Thrown while locating the serializer, which is synchronous, so it is caught here
            // rather than escaping into the host as an unhandled fault. It is a normal response -
            // the client asked for representations this operation does not have - and has to travel
            // the response path like any other.
            return WriteNotAcceptable(context, notAcceptable);
        }
    }

    /// <summary>
    /// A 406, with a body naming what the operation can produce.
    /// </summary>
    /// <remarks>
    /// The content type is committed before re-entering the locator, which does two things: it says
    /// plainly that this response is a JSON error document rather than one of the representations
    /// under negotiation, and it means the declared-set tier is not consulted a second time - so
    /// this cannot recurse into the refusal it is answering.
    /// </remarks>
    private Task WriteNotAcceptable(IExecutionContext context, NotAcceptableException exception) {
        context.Response.Status = exception.StatusCode;
        context.Response.ResponseValue = exception.Value;
        context.Response.ContentType = KnownContentType.Json;

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }

    private Task SerializeAcceptedResponse(IExecutionContext context) {
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

        // The status the operation declared, unless something along the way already chose one.
        //
        // Until now this was read in exactly one place - to write a "POST /books -> 201" doc comment
        // on the generated interface - and never applied. So a document promising 201 Created was
        // served as 200, and a client generated from that document was wrong about every creation
        // endpoint. The value was parsed, serialized between the build task and the generator, and
        // thrown away one step from where it was needed.
        var declared = context.HandlerInfo?.SuccessStatus;

        if (declared.HasValue && !context.Response.Status.HasValue) {
            context.Response.Status = declared.Value;
        }

        // A status defined to carry no body carries none, whatever the handler returned. Serializing
        // into a 204 produces a response no conforming client will read the body of and some
        // intermediaries will reject outright.
        if (CarriesNoBody(context.Response.Status)) {
            return Task.CompletedTask;
        }

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }

    /// <summary>
    /// The statuses RFC 9110 defines as having no content.
    /// </summary>
    /// <remarks>
    /// 205 is included on the same footing as 204: both are defined to have no body. 304 is here
    /// because a conditional response repeats headers alone.
    /// </remarks>
    private static bool CarriesNoBody(int? status) =>
        status is 204 or 205 or 304;

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
