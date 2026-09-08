using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.PubSub;

/// <summary>
/// Registers the Pub/Sub envelopes, applied to an application as <c>[PubSubModule]</c>.
/// </summary>
/// <remarks>
/// <para>
/// An application does not normally write this. <c>[Queue]</c> or <c>[Topic]</c> on a handler is
/// what selects it, through the <c>HardenedQueueModule</c> and <c>HardenedTopicModule</c> build
/// properties this package declares - one module for both, because a queue is a push subscription
/// and a topic is an Eventarc trigger on the topic, and both carry the same message.
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
        services.AddSingleton<ITriggerEnvelope, PubSubUnwrappedPushEnvelope>();
        services.AddSingleton<ITriggerEnvelope, PubSubTopicEnvelope>();
        services.AddSingleton<ITriggerEnvelope, PubSubPushEnvelope>();

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice registers each envelope once.</summary>
    public override bool Equals(object? obj) => obj is PubSubModule;

    public override int GetHashCode() => typeof(PubSubModule).GetHashCode();
}
