using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Application;

/// <summary>
/// One environment, reachable under both the type application code asks for and the type the
/// module system asks for.
/// </summary>
/// <remarks>
/// The second registration is the whole point. Without it the module system finds no
/// <see cref="IModuleEnvironment"/> in the collection and falls back to its own default, so
/// <c>[IfEnvironment]</c> answers against <c>ASPNETCORE_ENVIRONMENT</c> - defaulting to
/// <c>Production</c> - while everything else in the application answers against
/// <c>HARDENED_ENVIRONMENT</c>, defaulting to <c>development</c>.
/// </remarks>
public class HardenedEnvironmentServiceCollectionExtensionsTests {

    [Fact]
    public void TheEnvironmentIsRegisteredUnderBothServiceTypes() {
        var environment = new EnvironmentImpl("staging");

        var provider = new ServiceCollection()
            .AddHardenedEnvironment(environment)
            .BuildServiceProvider();

        Assert.Same(environment, provider.GetRequiredService<IHardenedEnvironment>());
        Assert.Same(environment, provider.GetRequiredService<IModuleEnvironment>());
    }

    /// <summary>
    /// Both service types must resolve the same object, not two that happen to agree today.
    /// </summary>
    [Fact]
    public void BothServiceTypesResolveTheOneInstance() {
        var provider = new ServiceCollection()
            .AddHardenedEnvironment(new EnvironmentImpl("qa"))
            .BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IHardenedEnvironment>(),
            provider.GetRequiredService<IModuleEnvironment>());
    }

    /// <summary>
    /// The module system reads the environment out of the collection while it is still being
    /// built, before any provider exists - so an instance is required and a factory would be
    /// invisible to it.
    /// </summary>
    [Fact]
    public void TheRegistrationCarriesAnInstanceRatherThanAFactory() {
        var services = new ServiceCollection().AddHardenedEnvironment(new EnvironmentImpl("test"));

        foreach (var serviceType in new[] { typeof(IHardenedEnvironment), typeof(IModuleEnvironment) }) {
            var descriptor = Assert.Single(services, service => service.ServiceType == serviceType);

            Assert.NotNull(descriptor.ImplementationInstance);
            Assert.Null(descriptor.ImplementationFactory);
        }
    }

    /// <summary>
    /// The name the module system sees is the Hardened one, which is the point of the pairing.
    /// </summary>
    [Fact]
    public void TheModuleSystemSeesTheHardenedEnvironmentName() {
        var provider = new ServiceCollection()
            .AddHardenedEnvironment(new EnvironmentImpl("staging"))
            .BuildServiceProvider();

        Assert.Equal("staging", provider.GetRequiredService<IModuleEnvironment>().EnvironmentName);
    }

    [Fact]
    public void TheArgumentsOverloadCarriesThemThrough() {
        var provider = new ServiceCollection()
            .AddHardenedEnvironment(new[] { "--verbose" })
            .BuildServiceProvider();

        Assert.Equal(new[] { "--verbose" }, provider.GetRequiredService<IHardenedEnvironment>().Arguments);
    }
}
