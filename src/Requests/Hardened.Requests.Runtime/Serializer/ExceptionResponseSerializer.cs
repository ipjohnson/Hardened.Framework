using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class ExceptionResponseSerializer : IExceptionResponseSerializer {
    private readonly IRequestLogger _requestLogger;
    private readonly ISerializationLocatorService _serializationLocatorService;
    private readonly IExceptionToModelConverter _exceptionToModelConverter;

    public ExceptionResponseSerializer(
        IRequestLogger requestLogger,
        ISerializationLocatorService serializationLocatorService,
        IExceptionToModelConverter exceptionToModelConverter) {
        _requestLogger = requestLogger;
        _serializationLocatorService = serializationLocatorService;
        _exceptionToModelConverter = exceptionToModelConverter;
    }

    /// <summary>
    /// Turns the exception into a response, and reports it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The logging is here because this is where every failure arrives.</b> It used to be
    /// attached to the producers instead, and they did not agree: a handler fault logged through
    /// <c>ControllerErrorHelper</c> and a bind failure through <c>RequestParameterBindFailed</c>,
    /// while an exception thrown by a filter, an authorization refusal and a rate-limit refusal
    /// logged nothing at all - four ways to fail a request, of which three were silent. Every one
    /// of them sets <c>Response.ExceptionValue</c>, and every one of them reaches this method.
    /// </para>
    /// <para>
    /// After the status is assigned rather than before, so the logger can read the status this
    /// failure actually answers with. That is what lets severity follow the answer - a declared
    /// 404 is a normal response and a 500 is a fault - without re-deriving the classification the
    /// converter has already done.
    /// </para>
    /// </remarks>
    public Task Handle(IExecutionContext context, Exception exp) {
        var (status, model) = _exceptionToModelConverter.ConvertExceptionToModel(context, exp);

        context.Response.Status = status;
        context.Response.ResponseValue = model;

        _requestLogger.RequestFailed(context, exp);

        return FindErrorSerializer(context).SerializeResponse(context);
    }

    /// <summary>
    /// The serializer for the error model, negotiated like an error rather than like the
    /// operation's success.
    /// </summary>
    /// <remarks>
    /// The operation's own representations get first refusal, so a caller of a JSON operation
    /// still gets JSON errors and an XML one XML. But those representations were declared for the
    /// success body, and an error model often is not writable as any of them - a
    /// <c>[RawResponse]</c> handler's committed <c>image/png</c>, or a <c>text/plain</c> operation
    /// whose raw writer takes only strings. That refusal used to escape as the locator's
    /// configuration fault and reach the caller as an empty 500. The content type is committed to
    /// JSON instead - the same move the 406 path makes, and committing rather than clearing is what
    /// keeps the second pass out of the declared-set tier that just refused - and located again.
    /// A <see cref="NotAcceptableException"/> is not caught: the client's stated preference is a
    /// different question, answered where it always was.
    /// </remarks>
    private IResponseSerializer FindErrorSerializer(IExecutionContext context) {
        try {
            return _serializationLocatorService.FindResponseSerializer(context);
        }
        catch (ContentTypeNotProducibleException) {
            context.Response.ContentType = KnownContentType.Json;

            return _serializationLocatorService.FindResponseSerializer(context);
        }
    }
}