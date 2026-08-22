using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
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

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }
}