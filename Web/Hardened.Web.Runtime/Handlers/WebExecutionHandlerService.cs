using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Runtime.Handlers
{
    public interface IWebExecutionHandlerService : IExecutionFilter
    {

    }
    
    public partial class WebExecutionHandlerService : IWebExecutionHandlerService
    {
        private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
        private readonly IResourceNotFoundHandler _resourceNotFoundHandler;
        private readonly IRequestLogger _requestLogger;

        public WebExecutionHandlerService(
            IEnumerable<IWebExecutionRequestHandlerProvider> handlers, 
            IResourceNotFoundHandler resourceNotFoundHandler, 
            IRequestLogger requestLogger)
        {
            _resourceNotFoundHandler = resourceNotFoundHandler;
            _requestLogger = requestLogger;
            _handlers = handlers.Reverse();
        }

        public Task Execute(IExecutionChain chain)
        {
            var context = chain.Context;

            foreach (var provider in _handlers)
            {
                var handler = provider.GetExecutionRequestHandler(context);

                if (handler != null)
                {
                    context.HandlerInfo = handler.HandlerInfo;

                    _requestLogger.RequestMapped(context);

                    var handlerChain = handler.GetExecutionChain(chain.Context);

                    return handlerChain.Next();
                }
            }

            return _resourceNotFoundHandler.Handle(chain);
        }
    }
}
