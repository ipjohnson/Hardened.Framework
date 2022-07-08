using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Lambda.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Web.Lambda.SUT
{
    [LambdaWebApplication]
    public partial class Application
    {
        private void RegisterFilters(IGlobalFilterRegistry registry)
        {
            var filter = new MetricsFilter();

            registry.RegisterFilter(filter);
        }

        private void RegisterDependencies(IServiceCollection serviceCollection)
        {

        }
    }
}