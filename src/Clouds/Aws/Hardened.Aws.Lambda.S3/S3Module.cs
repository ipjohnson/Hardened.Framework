using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Modules;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.S3;

/// <summary>
/// Registers the S3 adapter, applied to an application as <c>[S3Module]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Blob]</c> on a handler is what selects it,
/// through the <c>HardenedBlobModule</c> build property this package declares - which is also how
/// the same handler reaches a Blob Storage adapter on Azure.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class S3Module : IServiceCollectionConfiguration {
    /// <summary>
    /// No settings, unlike its siblings.
    /// </summary>
    /// <remarks>
    /// Every other batched adapter carries a <c>ReportBatchItemFailures</c> flag mirroring the event
    /// source mapping. S3 has nothing to mirror: it invokes a function asynchronously and reads no
    /// response, so a failed notification fails the invocation and Lambda's own retry and failure
    /// destination take it from there. A flag here would be a setting that changed nothing.
    /// </remarks>
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter>(new S3Adapter());

        services.AddBatchExecutionFilter();
    }

    /// <summary>
    /// By type alone, so applying the module twice loads one S3 adapter.
    /// </summary>
    /// <remarks>
    /// Declared rather than left to the generated default, which compares the same way but warns
    /// (DM0018) where a module has settable properties. This one has none, so the comparison is the
    /// whole of its identity and saying so is cheaper than reading the warning.
    /// </remarks>
    public override bool Equals(object? obj) => obj is S3Module;

    public override int GetHashCode() => typeof(S3Module).GetHashCode();
}
