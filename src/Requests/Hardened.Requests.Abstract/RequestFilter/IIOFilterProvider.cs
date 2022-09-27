using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter
{
    public interface IIOFilterProvider
    {
        IExecutionFilter ProvideFilter(
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest);
    }
}
