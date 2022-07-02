using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Handlers
{
    public interface IWebExecutionHandlerService : IExecutionFilter
    {

    }
    
    public class WebExecutionHandlerService : IWebExecutionHandlerService
    {
        private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;

        public WebExecutionHandlerService(IEnumerable<IWebExecutionRequestHandlerProvider> handlers)
        {
            _handlers = handlers.Reverse();
        }

        public Task Execute(IExecutionChain chain)
        {
            foreach (var provider in _handlers)
            {
                var handler = provider.GetExecutionRequestHandler(chain.Context);

                if (handler != null)
                {
                    Console.WriteLine("Found handler " + handler.GetType().FullName);

                    var handlerChain = handler.GetExecutionChain(chain.Context);

                    return handlerChain.Next();
                }
            }

            return chain.Next();
        }
    }
}
