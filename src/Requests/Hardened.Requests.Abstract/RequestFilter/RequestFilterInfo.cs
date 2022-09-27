using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter
{
    public class RequestFilterInfo
    {
        public RequestFilterInfo(Func<IExecutionContext, IExecutionFilter> filterFunc, int? order)
        {
            FilterFunc = filterFunc;
            Order = order;
        }

        public Func<IExecutionContext, IExecutionFilter> FilterFunc { get; }

        public int? Order { get; }
    }
}
