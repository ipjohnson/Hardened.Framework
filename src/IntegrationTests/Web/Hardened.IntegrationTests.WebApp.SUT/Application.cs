using Hardened.IntegrationTests.Web.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;

namespace Hardened.IntegrationTests.WebApp.SUT;

[HardenedModule]
[WebLibrary.Module("test")]
[AspNetCoreRuntime.Module]
public partial class Application {

    public static WebApplicationBuilder CreateBuilder(string[] args) {
        var hardenedApp = new Application();
        var environment = new EnvironmentImpl(arguments:  args);
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTransient<IHardenedEnvironment>(_ => environment);
        
        hardenedApp.ConfigureModule(environment, builder.Services);

        return builder;
    }
}