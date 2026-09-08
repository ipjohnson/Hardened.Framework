using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.PubSub;

/// <summary>
/// Registers the Pub/Sub push envelope, applied to an application as <c>[PubSubModule]</c>.
/// </summary>
/// <remarks>
/// <para>
/// An application does not normally write this. <c>[Queue]</c> on a handler is what selects it,
/// through the <c>HardenedQueueModule</c> build property this package declares - which is also
/// how the same handler reaches SQS on Lambda by changing one package reference.
/// </para>
/// <para>
/// Composes <c>[CloudRunRuntime]</c>, so a trigger attribute alone brings the host, the way a
/// Lambda adapter brings its runtime. The batch filter is registered even though a push carries
/// one message: the neutral test delivery sends every trigger as a batch, because every source
/// that carries one message can carry ten, and a fork carrying one item passes straight through.
/// </para>
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class PubSubModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope, PubSubPushEnvelope>();

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice registers one envelope.</summary>
    public override bool Equals(object? obj) => obj is PubSubModule;

    public override int GetHashCode() => typeof(PubSubModule).GetHashCode();
}
