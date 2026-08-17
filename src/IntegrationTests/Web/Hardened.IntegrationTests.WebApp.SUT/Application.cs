using Hardened.IntegrationTests.Web.SUT;
using DependencyModules.Runtime.Interfaces;
using Hardened.IntegrationTests.WebApp.SUT.Filters;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;

namespace Hardened.IntegrationTests.WebApp.SUT;

[HardenedModule]
[WebLibrary(Test = "test")]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    /// <summary>
    /// Serves the document the web generator wrote from this application's own routes.
    /// </summary>
    /// <remarks>
    /// On the module rather than in <c>CreateBuilder</c>, because that helper is only used by
    /// Program.cs - the test host calls <c>PopulateServiceCollection</c> directly, so a
    /// registration written there is one the tests never see.
    /// </remarks>
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IWebExecutionRequestHandlerProvider>(
            new OpenApiDocumentProvider(OpenApiDocument));

        // Stands in for authentication until the framework ships it, so the authorization tests can
        // exercise a caller who holds grants rather than only one who holds none.
        services.AddSingleton<IStartupService, TestPrincipalStartupService>();
    }

    public static WebApplicationBuilder CreateBuilder(string[] args) {
        var hardenedApp = new Application();
        var environment = new EnvironmentImpl(arguments:  args);
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTransient<IHardenedEnvironment>(_ => environment);

        hardenedApp.PopulateServiceCollection(builder.Services);

        return builder;
    }
}