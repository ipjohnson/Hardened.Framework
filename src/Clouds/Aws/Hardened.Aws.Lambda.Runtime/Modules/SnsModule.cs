using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// Registers the SNS adapter, applied to an application as <c>[SnsModule]</c>.
/// </summary>
/// <remarks>
/// Selected by <c>[Topic]</c> on a handler through the <c>HardenedTopicModule</c> build property.
/// An application that handles no topic carries neither the adapter nor
/// <c>Amazon.Lambda.SNSEvents.dll</c>, because this module is the only thing that references them.
/// </remarks>
[DependencyModule]
public partial class SnsModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, SnsAdapter>();
    }
}
