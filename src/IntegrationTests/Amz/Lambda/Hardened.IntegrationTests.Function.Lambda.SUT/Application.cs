using Hardened.IntegrationTests.Function.Lambda.SUT.Functions;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Function.Lambda.SUT;

[HardenedStartup]
public partial class Application
{
    private void RegisterDependencies(IServiceCollection serviceCollection)
    {
        serviceCollection.AddTransient<PersonFunctions>();
    }
}