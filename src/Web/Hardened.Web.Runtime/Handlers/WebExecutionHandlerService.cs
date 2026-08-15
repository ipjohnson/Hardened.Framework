using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.StaticContent;

namespace Hardened.Web.Runtime.Handlers;

public interface IWebExecutionHandlerService : IExecutionFilter { }

[SingletonService(Using = RegistrationType.Try)]
public partial class WebExecutionHandlerService : IWebExecutionHandlerService {
    private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
    private readonly IStaticContentHandler _staticContentHandler;
    private readonly IResourceNotFoundHandler _resourceNotFoundHandler;
    private readonly IMethodNotAllowedHandler _methodNotAllowedHandler;
    private readonly IRequestLogger _requestLogger;

    public WebExecutionHandlerService(
        IEnumerable<IWebExecutionRequestHandlerProvider> handlers,
        IResourceNotFoundHandler resourceNotFoundHandler,
        IMethodNotAllowedHandler methodNotAllowedHandler,
        IRequestLogger requestLogger,
        IStaticContentHandler staticContentHandler) {
        _resourceNotFoundHandler = resourceNotFoundHandler;
        _methodNotAllowedHandler = methodNotAllowedHandler;
        _requestLogger = requestLogger;
        _staticContentHandler = staticContentHandler;
        _handlers = handlers.Reverse();
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // What the tables that path-matched but verb-missed said was allowed. Collected across
        // every provider rather than answered by the first, because a later one may well have this
        // path under this verb - and a 405 from the first that recognised the path would shadow it.
        string? allow = null;

        foreach (var provider in _handlers) {
            var handler = provider.GetExecutionRequestHandler(context);

            if (handler == null) {
                continue;
            }

            if (handler.Handler == null) {
                allow = Merge(allow, handler.Allow);

                continue;
            }

            context.Request.PathTokens = handler.PathTokens;
            context.HandlerInfo = handler.Handler.HandlerInfo;

            _requestLogger.RequestMapped(context);

            var handlerChain = handler.Handler.GetExecutionChain(chain.Context);

            // A HEAD reaches the GET handler - the routing table sends it there - and must run
            // it in full to produce the same headers, so the body is dropped on the way out
            // rather than never asked for.
            if (HeadRequest.IsHead(context)) {
                return HeadRequest.ExecuteWithoutBody(handlerChain, context);
            }

            return handlerChain.Next();
        }

        return ResolvedFromSecondarySources(chain, context, allow);
    }

    private async Task ResolvedFromSecondarySources(
        IExecutionChain chain, IExecutionContext context, string? allow) {
        if (await _staticContentHandler.Handle(context)) {
            return;
        }

        // 405 before 404, and only after static content: a path that a table recognised under
        // another verb is a resource that exists, which is the whole distinction. API Gateway and
        // CloudFront cache the two differently, and a generated client reads them differently.
        if (allow != null) {
            await _methodNotAllowedHandler.Handle(context, allow);

            return;
        }

        await _resourceNotFoundHandler.Handle(chain);
    }

    /// <summary>
    /// Two tables' allowed verbs as one header value.
    /// </summary>
    /// <remarks>
    /// Rare - it needs two providers declaring the same path under different verbs - but the
    /// alternative is reporting one of them and leaving a client to conclude the other verb is
    /// unavailable when it is not.
    /// </remarks>
    private static string? Merge(string? existing, string? addition) {
        if (string.IsNullOrEmpty(addition)) {
            return existing;
        }

        if (string.IsNullOrEmpty(existing)) {
            return addition;
        }

        return existing + ", " + addition;
    }
}
