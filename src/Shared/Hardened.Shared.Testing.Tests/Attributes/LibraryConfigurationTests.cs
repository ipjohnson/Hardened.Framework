using DependencyModules.xUnit.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// A library's tests read the library's own configuration model. The library names no host, so
/// <c>HardenedCoreModule</c> reaches the container only because <c>[HardenedTestEntryPoint]</c>
/// loads it.
/// </summary>
[HardenedTestEntryPoint(typeof(OrdersLibrary))]
public class LibraryConfigurationTests
{
    [ModuleTest]
    public void ALibraryTestReadsTheModelsDefault(IOptions<IOrdersOptions> options)
    {
        Assert.Equal("orders", options.Value.QueueName);
    }

    [ModuleTest]
    [EnvironmentValue("ORDERS_QUEUE_NAME", "orders-test")]
    public void ALibraryTestReadsTheModelFromTheTestsEnvironment(IOptions<IOrdersOptions> options)
    {
        Assert.Equal("orders-test", options.Value.QueueName);
    }

    [ModuleTest]
    public void TheConfigurationManagerReturnsTheSameModel(
        IOptions<IOrdersOptions> options,
        IConfigurationManager configuration
    )
    {
        Assert.Same(options.Value, configuration.GetConfiguration<IOrdersOptions>());
    }
}
