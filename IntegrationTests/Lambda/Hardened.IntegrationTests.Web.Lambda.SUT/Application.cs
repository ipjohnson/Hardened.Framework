using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Lambda.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Web.Lambda.SUT
{
    [LambdaWebApplication]
    public partial class Application
    {
        private void GlobalFilters(IGlobalFilterRegistry registry)
        {

        }

        private void RegisterDependencies(IServiceCollection serviceCollection)
        {

        }
    }
}