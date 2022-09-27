using Hardened.Requests.Abstract.Execution;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Filters
{
    public class MetricsFilter : IExecutionFilter
    {

        public async Task Execute(IExecutionChain chain)
        {
            var startTime = DateTime.Now;

            await chain.Next();

            var totalTime = DateTime.Now - startTime;

        }
    }
}
