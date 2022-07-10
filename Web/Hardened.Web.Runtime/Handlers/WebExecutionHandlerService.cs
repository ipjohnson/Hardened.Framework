using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Runtime.Handlers
{
    public interface IWebExecutionHandlerService : IExecutionFilter
    {

    }
    
    public partial class WebExecutionHandlerService : IWebExecutionHandlerService
    {
        private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
        private readonly ILogger<WebExecutionHandlerService> _logger;

        public WebExecutionHandlerService(IEnumerable<IWebExecutionRequestHandlerProvider> handlers, ILogger<WebExecutionHandlerService> logger)
        {
            _logger = logger;
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
                    RequestHandlerLog(
                        handler.HandlerInfo.HandlerType.Name, 
                        handler.HandlerInfo.InvokeMethod, 
                        context.Request.Method, 
                        context.Request.Path);

                    var handlerChain = handler.GetExecutionChain(chain.Context);

                    return handlerChain.Next();
                }
            }

            return chain.Next();
        }

        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Information,
            Message = "{httpMethod} {path} handled by {className}.{methodName} ")]
        private partial void RequestHandlerLog(string className, string methodName, string httpMethod, string path);
    }
}
