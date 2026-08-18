using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.CacheControl;
using Hardened.Web.StaticContent;

namespace Hardened.IntegrationTests.StaticContent.SUT;

/// <summary>
/// An application that serves a directory of files, over the real pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A fixture of its own because static content is a property of the whole application: adding
/// <c>[HardenedStaticContent]</c> to the web application would put a fall-through mount underneath
/// every handler in it, and those handlers exist to test other things.
/// </para>
/// <para>
/// <b>No build items are declared</b>, so this exercises <c>FileSystemContentSource</c> - the
/// source an application gets by default, and the one where every protection has to be applied at
/// run time rather than by the build task.
/// </para>
/// <para>
/// <para>
/// Everything but the fall back file is set through <c>ConfigureStaticContent</c>, which is where
/// anything that is not a string has to be set: the generated module attribute unwraps
/// <c>Nullable&lt;T&gt;</c>, so a value-typed property would be copied onto the module carrying
/// <c>default(T)</c> whether or not it was written.
/// </para>
/// </remarks>
[HardenedModule]
[HardenedStaticContent(FallBackFile = "/index.html")]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.ConfigureStaticContent(content => {
            // The only way to see a Cache-Control at all, and Private rather than the default
            // Public because rendering anything but max-age is what the configuration could not
            // express until recently.
            content.CacheMaxAge = 3600;
            content.CacheControlType = CacheControlEnum.MaxAge | CacheControlEnum.Private;

            // Stated rather than inherited. A test run's environment is not "development", so this
            // would be on anyway - but a fixture that depends on that is one that changes answer
            // when somebody sets DOTNET_ENVIRONMENT.
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
