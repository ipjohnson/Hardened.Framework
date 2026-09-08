using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Eventarc;

/// <summary>
/// Registers the Eventarc envelope, applied to an application as <c>[EventarcModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Event]</c> on a handler is what selects it,
/// through the <c>HardenedEventModule</c> build property this package declares. The batch filter
/// is registered for the neutral test delivery, which sends every trigger as a batch; an event
/// carries one payload, and a fork carrying one item passes straight through.
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class EventarcModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope, EventarcEnvelope>();

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice registers one envelope.</summary>
    public override bool Equals(object? obj) => obj is EventarcModule;

    public override int GetHashCode() => typeof(EventarcModule).GetHashCode();
}
