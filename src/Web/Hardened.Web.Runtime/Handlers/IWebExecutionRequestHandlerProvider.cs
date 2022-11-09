using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.PathTokens;

namespace Hardened.Web.Runtime.Handlers;

public record RequestHandlerInfo(IExecutionRequestHandler Handler, PathTokenCollection PathTokens);

public interface IWebExecutionRequestHandlerProvider
{
    RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context);
}