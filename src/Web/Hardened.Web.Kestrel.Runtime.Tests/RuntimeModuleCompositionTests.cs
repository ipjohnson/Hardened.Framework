using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// What importing the runtime module on its own has to be enough for.
/// </summary>
/// <remarks>
/// The Kestrel host carried the same omission as <c>AspNetCoreRuntime</c> and
/// <c>LambdaWebModule</c> — it registered the host and nothing to serve with. It went unnoticed for
/// the same reason: the only application built on it declares <c>[HardenedWebModule]</c> itself, so
/// nothing ever exercised the module alone. That is precisely what this asserts.
/// </remarks>
public class RuntimeModuleCompositionTests {

    [Fact]
    public void KestrelRuntime_RegistersTheWebPipelineOnItsOwn() {
        var services = new ServiceCollection();

        // What a host supplies and the module does not.
        services.AddLogging();
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new KestrelRuntime().PopulateServiceCollection(services);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IWebExecutionHandlerService>());
    }
}
