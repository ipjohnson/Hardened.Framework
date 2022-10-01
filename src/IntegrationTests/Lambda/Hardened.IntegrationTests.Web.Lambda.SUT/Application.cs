using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.SUT;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Web.Lambda.SUT;

[HardenedStartup]
public partial class Application
{
    private IEnumerable<IApplicationModule> Modules()
    {
        yield return new WebLibraryEntryPoint();
    }

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