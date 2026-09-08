using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Blobs;

/// <summary>
/// Registers the blob adapter, applied to an application as <c>[BlobsModule]</c>.
/// </summary>
/// <remarks>
/// Selected by <c>[Blob]</c> on a handler through the <c>HardenedBlobModule</c> build property,
/// which is also how the same handler reaches S3 on Lambda. What an application writes here is
/// the app setting holding the storage connection, which the generator reads into the binding.
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class BlobsModule : IServiceCollectionConfiguration {
    /// <summary>
    /// The app setting that holds the storage connection, or null for the extension's default,
    /// <c>AzureWebJobsStorage</c>. Nullable, as every module property has to be; see
    /// <c>SqsModule</c>.
    /// </summary>
    public string? Connection { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new BlobsAdapter());

        // One blob per invocation is never a batch, but the neutral test delivery always is, and
        // the filter is what forks it per message; it passes an unbatched request straight through.
        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice loads one adapter.</summary>
    public override bool Equals(object? obj) => obj is BlobsModule;

    public override int GetHashCode() => typeof(BlobsModule).GetHashCode();
}
