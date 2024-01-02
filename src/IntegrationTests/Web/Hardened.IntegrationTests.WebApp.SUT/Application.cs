using Hardened.IntegrationTests.Web.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT;

[HardenedStartup]
public partial class Application {
    private IEnumerable<IApplicationModule> Modules() {
        yield return new WebLibraryEntryPoint();
        yield return new AspNetCoreRuntimeLibrary();
    }

    public void RegisterDependencies(IServiceCollection collection, IEnvironment environment) {
        collection.AddTransient<IEnvironment>(_ => environment);
        WebRuntimeDI.Register(environment, collection);
    }
}