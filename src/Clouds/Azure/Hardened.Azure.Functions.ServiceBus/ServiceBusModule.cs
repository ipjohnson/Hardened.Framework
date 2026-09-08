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
/// An application does not normally write this. <c>[Queue]</c> on a handler is what selects it,
/// through the <c>HardenedQueueModule</c> build property this package's targets set - which is
/// also how the same handler reaches SQS on Lambda by changing a package reference.
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
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new ServiceBusAdapter());

        services.AddBatchExecutionFilter();
    }

    /// <summary>
    /// By type alone, so applying the module twice loads one Service Bus adapter.
    /// </summary>
    public override bool Equals(object? obj) => obj is ServiceBusModule;

    public override int GetHashCode() => typeof(ServiceBusModule).GetHashCode();
}
