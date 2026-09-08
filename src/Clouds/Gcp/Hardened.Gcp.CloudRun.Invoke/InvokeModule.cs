using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Invoke;

/// <summary>
/// Registers the direct-invocation envelope, applied to an application as <c>[InvokeModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[HardenedFunction]</c> on a handler is what
/// selects it, through the <c>HardenedInvokeModule</c> build property this package declares. It
/// is written out only to change <see cref="Prefix"/>.
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class InvokeModule : IServiceCollectionConfiguration {
    /// <summary>
    /// The path an invocation names its operation under, or null for
    /// <see cref="InvokeEnvelope.DefaultPrefix"/>.
    /// </summary>
    /// <remarks>
    /// Nullable, as every module property has to be: DependencyModules copies a property across
    /// guarded by a null check only for a nullable one.
    /// </remarks>
    public string? Prefix { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope>(new InvokeEnvelope(Prefix ?? InvokeEnvelope.DefaultPrefix));
    }

    /// <summary>By type alone, so applying the module twice registers one envelope.</summary>
    public override bool Equals(object? obj) => obj is InvokeModule;

    public override int GetHashCode() => typeof(InvokeModule).GetHashCode();
}
