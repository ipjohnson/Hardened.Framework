using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;

namespace Hardened.Web.Runtime.Handlers;

public class RequestHandlerInfo
{
    public RequestHandlerInfo(IExecutionRequestHandler handler, IPathTokenCollection pathTokens)
    {
        Handler = handler;
        PathTokens = pathTokens;
    }

    public IExecutionRequestHandler Handler { get;  }

    public IPathTokenCollection PathTokens { get; }
}

public interface IWebExecutionRequestHandlerProvider
{
    IExecutionRequestHandler? GetExecutionRequestHandler(IExecutionContext context);
}