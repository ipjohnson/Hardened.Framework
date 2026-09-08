using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Firestore;

/// <summary>
/// Registers the Firestore envelope, applied to an application as <c>[FirestoreModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Change]</c> on a handler is what selects it,
/// through the <c>HardenedChangeModule</c> build property this package declares - which is also
/// how the same handler reaches a DynamoDB stream on Lambda. The batch filter is registered for
/// the neutral test delivery, which sends every trigger as a batch; a document event carries one
/// document.
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class FirestoreModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope, FirestoreEnvelope>();

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice registers one envelope.</summary>
    public override bool Equals(object? obj) => obj is FirestoreModule;

    public override int GetHashCode() => typeof(FirestoreModule).GetHashCode();
}
