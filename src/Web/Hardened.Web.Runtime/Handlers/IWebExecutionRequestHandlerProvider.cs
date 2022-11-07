using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;

namespace Hardened.Web.Runtime.Handlers;

public record RequestHandlerInfo(IExecutionRequestHandler Handler, IPathTokenCollection PathTokens);

public interface IWebExecutionRequestHandlerProvider
{
    RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context);
}