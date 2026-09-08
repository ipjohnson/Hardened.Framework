using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Scheduler;

/// <summary>
/// Registers the Cloud Scheduler envelope, applied to an application as <c>[SchedulerModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Timer]</c> on a handler is what selects it,
/// through the <c>HardenedTimerModule</c> build property this package declares. It is written out
/// only to change <see cref="Prefix"/>, which is a deployment's decision about its own URLs.
/// </remarks>
[DependencyModule]
[CloudRunRuntime]
public partial class SchedulerModule : IServiceCollectionConfiguration {
    /// <summary>
    /// The path a job's target URL carries the timer's name under, or null for
    /// <see cref="SchedulerEnvelope.DefaultPrefix"/>.
    /// </summary>
    /// <remarks>
    /// Nullable, as every module property has to be: DependencyModules copies a property across
    /// guarded by a null check only for a nullable one.
    /// </remarks>
    public string? Prefix { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerEnvelope>(new SchedulerEnvelope(Prefix ?? SchedulerEnvelope.DefaultPrefix));

        // For the neutral test delivery, which sends every trigger as a batch; a schedule fires
        // once, and a fork carrying one item passes straight through.
        services.AddBatchExecutionFilter();
    }

    /// <summary>
    /// By type alone, so applying the module twice registers one envelope; two declarations
    /// disagreeing about the prefix is a contradiction about one deployment's URLs.
    /// </summary>
    public override bool Equals(object? obj) => obj is SchedulerModule;

    public override int GetHashCode() => typeof(SchedulerModule).GetHashCode();
}
