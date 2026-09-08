using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.EventHubs;

/// <summary>
/// Registers the Event Hubs adapter, applied to an application as <c>[EventHubsModule]</c>.
/// </summary>
/// <remarks>
/// Selected by <c>[Stream]</c> on a handler through the <c>HardenedStreamModule</c> build property,
/// which is also how the same handler reaches Kinesis on Lambda. What an application writes here
/// are deployment facts the generator reads into the function's binding: the app setting holding
/// the connection, and the consumer group.
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class EventHubsModule : IServiceCollectionConfiguration {
    /// <summary>
    /// The app setting that holds the Event Hubs connection, or null for
    /// <c>AzureWebJobsEventHubs</c>. The extension has no default of its own - a trigger naming
    /// no connection fails the host at startup - so the generator writes that one, named after
    /// the Service Bus extension's. Nullable, as every module property has to be; see
    /// <c>SqsModule</c>.
    /// </summary>
    public string? Connection { get; set; }

    /// <summary>The consumer group the functions read through, or null for <c>$Default</c>.</summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// How many times the host invokes a function again after a failed invocation, or null for
    /// no retry. Written with <see cref="RetryDelay"/>, or not at all.
    /// </summary>
    /// <remarks>
    /// The host advances the partition's checkpoint when an invocation completes, failed or not,
    /// so a retry policy is the one way a thrown batch is delivered again; see
    /// <see cref="EventHubsAdapter"/>. The generator writes the two properties as the worker's
    /// <c>[FixedDelayRetry]</c> on every stream function and into the metadata the host indexes.
    /// </remarks>
    public int? RetryCount { get; set; }

    /// <summary>
    /// The wait between one attempt and the next, as <c>hh:mm:ss</c>, or null for no retry.
    /// Written with <see cref="RetryCount"/>, or not at all.
    /// </summary>
    public string? RetryDelay { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new EventHubsAdapter());

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice loads one adapter.</summary>
    public override bool Equals(object? obj) => obj is EventHubsModule;

    public override int GetHashCode() => typeof(EventHubsModule).GetHashCode();
}
