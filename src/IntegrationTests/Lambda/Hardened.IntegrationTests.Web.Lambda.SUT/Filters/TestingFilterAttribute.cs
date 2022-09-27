using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Filters
{
    public class TestingFilterAttribute : Attribute, IRequestFilterProvider
    {
        public int TestValue { get; set; }

        public int OtherValue { get; set; }

        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
        {
            yield return new RequestFilterInfo(c => new TestingFilter(), FilterOrder.DefaultValue);
        }
    }
    
    public class TestingFilter : IExecutionFilter
    {
        public Task Execute(IExecutionChain chain)
        {
            return chain.Next();
        }
    }
}
