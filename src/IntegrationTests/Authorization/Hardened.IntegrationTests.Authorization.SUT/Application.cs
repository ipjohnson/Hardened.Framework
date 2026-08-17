using DependencyModules.Runtime.Interfaces;
using Hardened.IntegrationTests.Authorization.SUT.Filters;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;

namespace Hardened.IntegrationTests.Authorization.SUT;

/// <summary>
/// An application in the default-deny posture.
/// </summary>
/// <remarks>
/// <para>
/// A fixture of its own rather than another controller in the web application, because
/// <c>[RequireAuthorization]</c> is a property of the whole application: adding it there would
/// change the answer for every handler in it, and those handlers exist to test other things.
/// </para>
/// <para>
/// The two postures are different enough to be worth testing separately. The web application covers
/// the first rung of the ladder - nothing said, every handler public - and this covers the second,
/// where saying nothing is what gets a handler refused.
/// </para>
/// </remarks>
[HardenedModule]
[RequireAuthorization]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        // Stands in for authentication until the framework ships it, so these tests can exercise a
        // caller who holds grants rather than only one who holds none.
        services.AddSingleton<IStartupService, TestPrincipalStartupService>();
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
