using Hardened.IntegrationTests.Web.SUT;
using DependencyModules.Runtime.Interfaces;
using Hardened.IntegrationTests.WebApp.SUT.Filters;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;

namespace Hardened.IntegrationTests.WebApp.SUT;

/// <remarks>
/// <c>[Enable&lt;HardenedOpenApiDocument&gt;]</c> is what embeds the document the web generator wrote
/// from this application's own routes and serves it at <c>/openapi.json</c>. It replaces the
/// registration this module used to make by hand, which had to live here rather than in
/// <c>CreateBuilder</c> - that helper is only used by Program.cs, and the test host calls
/// <c>PopulateServiceCollection</c> directly.
/// </remarks>
[HardenedModule]
[WebLibrary(Test = "test")]
[Enable<HardenedOpenApiDocument>]
[HardenedOpenApiUi(Title = "Integration Tests")]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
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