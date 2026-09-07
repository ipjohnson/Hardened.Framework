using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// Registers the EventBridge adapter, applied to an application as <c>[EventBridgeModule]</c>.
/// </summary>
/// <remarks>
/// Selected by either <c>[Timer]</c> or <c>[Event]</c>, so <c>HardenedTimerModule</c> and
/// <c>HardenedEventModule</c> both name it - one adapter serving two triggers, because a schedule
/// and a bus event arrive in the same envelope and differ only in their source. Applying it twice
/// is harmless: modules deduplicate by equality.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class EventBridgeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, EventBridgeAdapter>();
    }
}
