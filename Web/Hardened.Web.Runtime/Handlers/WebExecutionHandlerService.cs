﻿using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Web.Runtime.StaticContent;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Runtime.Handlers
{
    public interface IWebExecutionHandlerService : IExecutionFilter
    {

    }
    
    public partial class WebExecutionHandlerService : IWebExecutionHandlerService
    {
        private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
        private readonly IStaticContentHandler _staticContentHandler;
        private readonly IResourceNotFoundHandler _resourceNotFoundHandler;
        private readonly IRequestLogger _requestLogger;

        public WebExecutionHandlerService(
            IEnumerable<IWebExecutionRequestHandlerProvider> handlers, 
            IResourceNotFoundHandler resourceNotFoundHandler, 
            IRequestLogger requestLogger, 
            IStaticContentHandler staticContentHandler)
        {
            _resourceNotFoundHandler = resourceNotFoundHandler;
            _requestLogger = requestLogger;
            _staticContentHandler = staticContentHandler;
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

            if (_staticContentHandler.CanHandleRequest(context))
            {
                return _staticContentHandler.HandleRequest(context);
            }

            return _resourceNotFoundHandler.Handle(chain);
        }
    }
}
