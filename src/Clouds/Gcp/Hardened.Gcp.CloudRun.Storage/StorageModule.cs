using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Storage;

/// <summary>
/// Registers the Cloud Storage envelope, applied to an application as <c>[StorageModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Blob]</c> on a handler is what selects it,
/// through the <c>HardenedBlobModule</c> build property this package declares - which is also how
/// the same handler reaches S3 on Lambda. The batch filter is registered for the neutral test
/// delivery, which sends every trigger as a batch; a notification carries one object.
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class StorageModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope, StorageEnvelope>();

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice registers one envelope.</summary>
    public override bool Equals(object? obj) => obj is StorageModule;

    public override int GetHashCode() => typeof(StorageModule).GetHashCode();
}
