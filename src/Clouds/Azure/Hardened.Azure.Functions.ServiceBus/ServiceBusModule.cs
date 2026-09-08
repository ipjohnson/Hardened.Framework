using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.ServiceBus;

/// <summary>
/// Registers the Service Bus adapter, applied to an application as <c>[ServiceBusModule]</c>.
/// </summary>
/// <remarks>
/// <para>
/// An application does not normally write this. <c>[Queue]</c> or <c>[Topic]</c> on a handler is
/// what selects it, through the <c>HardenedQueueModule</c> and <c>HardenedTopicModule</c> build
/// properties this package's targets set - which is also how the same handler reaches SQS and SNS
/// on Lambda by changing a package reference.
/// </para>
/// <para>
/// <b>What an application does write here are deployment facts</b>, the way
/// <c>[SqsModule(ReportBatchItemFailures = true)]</c> is written on Lambda: the subscription a
/// topic handler consumes through, the app setting holding the connection, and whether messages
/// are settled one by one. The generator reads the same attribute to write the function's binding,
/// so what the host is told and what the adapter does cannot disagree.
/// </para>
/// <para>
/// A module rather than an unconditional registration, for the reason <c>SqsModule</c> gives:
/// nothing references the adapter until an application applies the module, so a function that
/// handles no queue carries no adapter and no Service Bus SDK.
/// </para>
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class ServiceBusModule : IServiceCollectionConfiguration {
    /// <summary>
    /// The subscription every <c>[Topic]</c> handler in the application consumes through.
    /// </summary>
    /// <remarks>
    /// One name for the module rather than one per topic, because a subscription is named for the
    /// consumer rather than for what it consumes: an application subscribing to three topics is
    /// ordinarily the same subscription name on each. A topic handler with no subscription named
    /// is HRDAZ003 at build, because a Service Bus topic cannot be read without one. Nullable, as
    /// every module property has to be; see <c>SqsModule</c>.
    /// </remarks>
    public string? Subscription { get; set; }

    /// <summary>
    /// The app setting that holds the Service Bus connection, or null for the extension's own
    /// default, <c>AzureWebJobsServiceBus</c>.
    /// </summary>
    public string? Connection { get; set; }

    /// <summary>
    /// Whether a failed message is settled on its own rather than failing the whole invocation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, as it is for SQS, and for a matching reason. On, the generated function
    /// turns the host's auto-completion off, every message the handler accepted is completed and
    /// every one it refused is abandoned, so a poison message redelivers alone. Off, the host
    /// completes or abandons the batch on the invocation's outcome, and a refused message takes
    /// the batch with it.
    /// </para>
    /// <para>
    /// It has to match what the deployment can tolerate: with settlement on, a handler that is not
    /// idempotent sees each accepted message once, and one that failed before the chain ran sees
    /// the batch again when the locks expire rather than at once.
    /// </para>
    /// </remarks>
    public bool? ReportsItemFailures { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new ServiceBusAdapter(ReportsItemFailures ?? false));

        services.AddBatchExecutionFilter();
    }

    /// <summary>
    /// By type alone, so applying the module twice loads one Service Bus adapter.
    /// </summary>
    /// <remarks>
    /// Declared rather than left to the generated default, which compares the same way but warns
    /// (DM0018) because a settable property then makes the first instance reached win. That is the
    /// intended behaviour: two declarations disagreeing about a deployment fact is a contradiction
    /// about a single deployment.
    /// </remarks>
    public override bool Equals(object? obj) => obj is ServiceBusModule;

    public override int GetHashCode() => typeof(ServiceBusModule).GetHashCode();
}
