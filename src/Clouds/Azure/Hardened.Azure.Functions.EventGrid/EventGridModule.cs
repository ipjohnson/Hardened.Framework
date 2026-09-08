using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.EventGrid;

/// <summary>
/// Registers the Event Grid adapter, applied to an application as <c>[EventGridModule]</c>.
/// </summary>
/// <remarks>
/// Selected by <c>[Event]</c> on a handler through the <c>HardenedEventModule</c> build property,
/// which is also how the same handler reaches EventBridge on Lambda and Eventarc on Cloud Run. No
/// settings: an Event Grid subscription is wired to the function by the deployment, and the
/// function reads whatever it is sent.
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class EventGridModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new EventGridAdapter());

        // One event per invocation is never a batch, but the neutral test delivery always is, and
        // the filter is what forks it per message; it passes an unbatched request straight through.
        services.AddBatchExecutionFilter();
    }

    public override bool Equals(object? obj) => obj is EventGridModule;

    public override int GetHashCode() => typeof(EventGridModule).GetHashCode();
}
