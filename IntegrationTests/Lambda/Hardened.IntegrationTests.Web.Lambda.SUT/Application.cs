using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Logging;
using Hardened.Web.Lambda.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Web.Lambda.SUT
{
    [LambdaWebApplication]
    public partial class Application
    {
        private void ConfigureLogging(IEnvironment environment, ILoggingBuilder builder)
        {
            builder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Hardened", LogLevel.Debug)
                .AddLambdaLogger();
        }

        private async Task Startup(IServiceProvider rootProvider)
        {
            
        }

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