using Amazon.CloudWatchLogs;
using Hardened.Amz.Canaries.Runtime.Discovery;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Canaries.Runtime;

[HardenedStartup]
public partial class CanaryApplication
{
    protected virtual Task Startup(IServiceProvider provider)
    {
        provider.GetRequiredService<ICanaryDiscoveryService>();
        
        return Task.CompletedTask;
    }
    
    protected virtual IEnumerable<IApplicationModule> Modules()
    {
        yield return new DynamoDbClientLibrary();
    }

    protected virtual void RegisterDependencies(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IXunitAssemblyUnderTest>(
            _ => new XunitAssemblyUnderTest(GetType().Assembly)
        );
    }
}