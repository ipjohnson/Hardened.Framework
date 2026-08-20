using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class NullValueResponseHandler : INullValueResponseHandler {
    private readonly ILogger<NullValueResponseHandler> _logger;
    private readonly ISerializationLocatorService _serializationLocatorService;

    public NullValueResponseHandler(
        ILogger<NullValueResponseHandler> logger,
        ISerializationLocatorService serializationLocatorService) {
        _logger = logger;
        _serializationLocatorService = serializationLocatorService;
    }

    public Task Handle(IExecutionContext context) {
        if (context.HandlerInfo?.NullResponseStatus.HasValue ?? false) {
            context.Response.Status = context.HandlerInfo.NullResponseStatus.Value;
        }
        else {
            switch (context.Request.Method) {
                case "GET":
                    context.Response.Status = 404;
                    break;
                case "POST":
                    context.Response.Status = 200;
                    break;
                case "PUT":
                    context.Response.Status = 404;
                    break;
                case "DELETE":
                    context.Response.Status = 200;
                    break;
                default:
                    context.Response.Status = 200;
                    break;
            }
        }

        // Where a null result is success, the operation's declared success status is what success
        // means - so a DELETE declaring 204 answers 204 rather than the table's generic 200.
        //
        // Not where the result is a 404. Null on a GET means the handler found nothing, and an
        // operation declaring 201 for its success does not thereby declare that a miss is a 201.
        // The two readings of null part here, which is the only place they can.
        if (context.Response.Status is >= 200 and < 300 &&
            context.HandlerInfo?.SuccessStatus is { } declared) {
            context.Response.Status = declared;
        }

        if (context.Response.Status == 404) {
            _logger.LogInformation("Could not find resource {0} {1}", context.Request.Method, context.Request.Path);
        }

        // A status defined to carry no body carries none - the same rule the success path applies,
        // and the one a declared 204 is usually reaching for.
        if (context.Response.Status is 204 or 205 or 304) {
            return Task.CompletedTask;
        }

        // The body the description declared for this status, when it declared one.
        //
        // This wrote a status and stopped, so an operation whose document promised a Problem for its
        // 404 answered with an empty body - a response the contract does not describe and a
        // generated client cannot read. The instance is built at generation time and holds the
        // status and its reason phrase only: shaped as the contract says, and saying nothing about
        // why the handler found nothing. A handler wanting to say more throws the declared exception
        // type, which carries a body it wrote.
        var body = context.HandlerInfo?.NullResponseBody;

        if (body == null) {
            return Task.CompletedTask;
        }

        context.Response.ResponseValue = body;

        return _serializationLocatorService.FindResponseSerializer(context).SerializeResponse(context);
    }
}