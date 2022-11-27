using Hardened.Amz.Canaries.Runtime.Attributes;
using Hardened.Amz.Canaries.Runtime.Discovery;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.Canaries.Runtime.Tests.Canaries;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Amz.Canaries.Runtime.Tests.Discovery;

public class CanaryDiscoveryServiceTests
{
    [HardenedTest]
    public void DiscoverCanaries(CanaryDiscoveryService discoveryService)
    {
        Assert.Equal(6, discoveryService.CanaryDefinitions.Count);
    }

    public void RegisterDependencies(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<ICanaryTypeRegistration>(
            new CanaryTypeRegistration(typeof(CanaryTests)));
    }
    
}