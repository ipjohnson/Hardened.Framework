using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Handlers
{
    public interface IWebExecutionRequestHandlerProvider
    {
        IExecutionRequestHandler? GetExecutionRequestHandler(IExecutionContext context);
    }
}
