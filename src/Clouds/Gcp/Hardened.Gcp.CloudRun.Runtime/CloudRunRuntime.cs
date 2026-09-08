using System.Diagnostics.CodeAnalysis;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Hardened.Gcp.CloudRun.Runtime.Dispatch;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Gcp.CloudRun.Runtime;

/// <summary>
/// Hosting for Hardened on Google Cloud Run: Kestrel, with a trigger front door ahead of routing.
/// </summary>
/// <remarks>
/// <para>
/// Everything reaches a Cloud Run service as HTTP, so the request is already the one Kestrel
/// built. What this module adds is what Cloud Run adds: <see cref="TriggerFrontDoor"/> in the
/// middleware chain, which recognises the envelope a Pub/Sub push or an Eventarc event arrives in
/// and forks the chain with the trigger-shaped request it builds; and one dispatch for a service
/// that serves web routes and triggers over the same socket, composed by
/// <see cref="CloudRunDispatch.Compose"/>. An application names this the way it names any host,
/// <c>[CloudRunRuntime]</c> beside <c>[HardenedModule]</c>, and an adapter package composes it
/// the way the Lambda adapters compose their runtime, so a trigger attribute alone is enough to
/// bring it.
/// </para>
/// <para>
/// <b>The dispatch is composed as a DependencyModules decorator, not in
/// <see cref="ConfigureServices"/>.</b> Modules are applied dependencies first and the application
/// last, so when this module's services are configured the routing table is registered and the
/// function dispatch the generator emits into the application is not - a replacement made here
/// would be added to rather than replaced. Decorators run after every module's services, which is
/// what the hook is for: "a function that decorates registrations already in the collection".
/// </para>
/// </remarks>
[DependencyModule]
[KestrelRuntime]
public partial class CloudRunRuntime : IServiceCollectionConfiguration {

    /// <summary>
    /// A static field initializer merges into the type's static constructor, so this is
    /// registered before the first instance exists and before the registry is read - the same
    /// arrangement the function generator uses for its own registrations.
    /// </summary>
    [DynamicDependency(nameof(ComposeDispatch))]
    private static readonly int _composeDispatch =
        DependencyRegistry<CloudRunRuntime>.AddDecorator(ComposeDispatch);

    public void ConfigureServices(IServiceCollection services) {
        // TryAddEnumerable rather than Add: a startup service registered twice runs twice, and
        // this one puts the front door in the middleware chain - so a second registration would
        // unwrap every envelope twice. An adapter module composing this one beside an application
        // that also declared it reaches here twice.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupService, TriggerFrontDoorStartupService>());
    }

    private static void ComposeDispatch(IServiceCollection services) =>
        CloudRunDispatch.Compose(services);
}
