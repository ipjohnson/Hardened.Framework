using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Attributes
{
    public interface IExecutionFilterProviderAttribute
    {
        Func<IServiceProvider, IExecutionFilter> ProvideFilter();
    }
}
