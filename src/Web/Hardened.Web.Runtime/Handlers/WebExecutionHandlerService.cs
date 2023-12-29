using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.StaticContent;

namespace Hardened.Web.Runtime.Handlers;

public interface IWebExecutionHandlerService : IExecutionFilter { }

public partial class WebExecutionHandlerService : IWebExecutionHandlerService {
    private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
    private readonly IStaticContentHandler _staticContentHandler;
    private readonly IResourceNotFoundHandler _resourceNotFoundHandler;
    private readonly IRequestLogger _requestLogger;

    public WebExecutionHandlerService(
        IEnumerable<IWebExecutionRequestHandlerProvider> handlers,
        IResourceNotFoundHandler resourceNotFoundHandler,
        IRequestLogger requestLogger,
        IStaticContentHandler staticContentHandler) {
        _resourceNotFoundHandler = resourceNotFoundHandler;
        _requestLogger = requestLogger;
        _staticContentHandler = staticContentHandler;
        _handlers = handlers.Reverse();
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        foreach (var provider in _handlers) {
            var handler = provider.GetExecutionRequestHandler(context);

            if (handler != null) {
                context.Request.PathTokens = handler.PathTokens;
                context.HandlerInfo = handler.Handler.HandlerInfo;

                _requestLogger.RequestMapped(context);

                var handlerChain = handler.Handler.GetExecutionChain(chain.Context);

                return handlerChain.Next();
            }
        }

        return ResolvedFromSecondarySources(chain, context);
    }

    private async Task ResolvedFromSecondarySources(IExecutionChain chain, IExecutionContext context) {
        if (await _staticContentHandler.Handle(context)) { }
        else {
            await _resourceNotFoundHandler.Handle(chain);
        }
    }
}