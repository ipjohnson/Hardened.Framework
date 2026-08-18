using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.CacheControl;
using Hardened.Web.StaticContent;

namespace Hardened.IntegrationTests.StaticContent.Manifest.SUT;

/// <summary>
/// The same content, served from a manifest the build produced.
/// </summary>
/// <remarks>
/// <para>
/// A second fixture rather than a flag on the first, because which source answers is decided by
/// whether the project declares <c>&lt;HardenedStaticContent&gt;</c> items - a build-time fact, not
/// a runtime one. The two read the same directory, linked rather than copied, so a test asserting
/// they behave alike is not comparing two different trees.
/// </para>
/// <para>
/// <b>This is also the only thing that exercises the targets file.</b> Nothing else in the
/// repository consumes it, so without this the MSBuild task, its up-to-date check and the generated
/// manifest reaching the compilation are all unverified.
/// </para>
/// </remarks>
[HardenedModule]
[HardenedStaticContent]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.ConfigureStaticContent(content => {
            content.CacheMaxAge = 3600;
            content.CacheControlType = CacheControlEnum.MaxAge | CacheControlEnum.Private;
            content.CacheContent = true;
        });
    }

    public static WebApplicationBuilder CreateBuilder(string[] args) {
        var hardenedApp = new Application();
        var environment = new EnvironmentImpl(arguments: args);
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTransient<IHardenedEnvironment>(_ => environment);

        hardenedApp.PopulateServiceCollection(builder.Services);

        return builder;
    }
}
