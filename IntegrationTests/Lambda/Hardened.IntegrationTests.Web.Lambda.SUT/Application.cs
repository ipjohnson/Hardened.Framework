using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Logging;
using Hardened.Web.Lambda.Runtime;
using Hardened.Web.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Web.Lambda.SUT
{
    [LambdaWebApplication]
    public partial class Application
    {
        private void Configure(IAppConfig config)
        {
            config.Amend<StaticContentConfiguration>( 
                staticConfig => staticConfig.CacheMaxAge = 15);

            config.Amend<ResponseHeaderConfiguration>(responseHeader =>
            {
                responseHeader.Add("TestResponseHeader", "TestValue");
                responseHeader.Add(c => c.Response.Headers.Set("OtherTest", "OtherValue"));
            });
        }

        //private void ConfigureLogging(IEnvironment environment, ILoggingBuilder builder)
        //{
        //    builder
        //        .AddFilter("Microsoft", LogLevel.Warning)
        //        .AddFilter("System", LogLevel.Warning)
        //        .AddFilter("Hardened", LogLevel.Debug)
        //        .AddLambdaLogger();
        //}

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