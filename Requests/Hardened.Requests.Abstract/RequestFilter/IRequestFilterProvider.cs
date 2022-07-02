using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter
{
    public interface IRequestFilterProvider
    {
        IEnumerable<Func<IServiceProvider, IExecutionFilter>> GetFilters(IExecutionRequestHandlerInfo handlerInfo);
    }
}
