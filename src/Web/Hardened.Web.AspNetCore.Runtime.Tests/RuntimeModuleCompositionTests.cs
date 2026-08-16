using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Logging;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests;

/// <summary>
/// What importing the runtime module on its own has to be enough for.
/// </summary>
/// <remarks>
/// <para>
/// The README's Quick Start attributes an application with <c>[HardenedModule]</c> and
/// <c>[AspNetCoreRuntime]</c> and nothing else. That shape threw on the first request —
/// <c>No service for type 'IWebExecutionHandlerService' has been registered</c> — because
/// <c>AspNetCoreRuntime</c> registered the host and nothing to serve with.
/// </para>
/// <para>
/// It survived a green integration suite because neither web sample is that shape: one declares
/// <c>[HardenedWebModule]</c> explicitly and the other inherits it from a library referenced for
/// unrelated reasons. So the test is not "does the pipeline work" — it is "does this module, alone,
/// register something to serve with," which is the only question the samples cannot answer.
/// </para>
/// </remarks>
public class RuntimeModuleCompositionTests {

    [Fact]
    public void AspNetCoreRuntime_RegistersTheWebPipelineOnItsOwn() {
        var services = new ServiceCollection();

        // What a host supplies and the module does not: WebApplication.CreateBuilder adds logging,
        // and IHardenedEnvironment is registered by the application because only it knows where its
        // name and arguments come from.
        services.AddLogging();
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new AspNetCoreRuntime().PopulateServiceCollection(services);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IWebExecutionHandlerService>());
    }

    /// <summary>
    /// Declaring the web module as well is the shape both existing samples use, and it must stay
    /// harmless — modules deduplicate by equality, so the import is idempotent.
    /// </summary>
    [Fact]
    public void AspNetCoreRuntime_ToleratesTheWebModuleBeingDeclaredTwice() {
        var services = new ServiceCollection();

        // What a host supplies and the module does not: WebApplication.CreateBuilder adds logging,
        // and IHardenedEnvironment is registered by the application because only it knows where its
        // name and arguments come from.
        services.AddLogging();
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new AspNetCoreRuntime().PopulateServiceCollection(services);
        new Hardened.Web.Runtime.DependencyInjection.HardenedWebModule()
            .PopulateServiceCollection(services);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IWebExecutionHandlerService>());
    }
}
