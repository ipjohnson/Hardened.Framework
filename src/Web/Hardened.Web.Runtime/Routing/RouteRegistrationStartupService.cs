using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Runs every <see cref="IRouteRegistration"/> once, then closes the table.
/// </summary>
/// <remarks>
/// <para>
/// Registration has to finish before the first request, and a startup service is where that
/// happens: <c>ApplicationLogic.RunApplication</c> awaits every one of them before the host is
/// handed the delegate that starts serving.
/// </para>
/// <para>
/// <b>One caveat, and it is the one to know about.</b> Startup services all run concurrently under a
/// single <c>Task.WhenAll</c>, so a registration that depends on another startup service's work has
/// no ordering guarantee against it. Depend on a service the container can build, not on a side
/// effect another startup service leaves behind.
/// </para>
/// </remarks>
public class RouteRegistrationStartupService : IStartupService
{
    public async Task<bool> Startup(IServiceProvider rootProvider)
    {
        var provider = rootProvider.GetRequiredService<RegisteredRouteProvider>();

        if (provider.IsReady)
        {
            return true;
        }

        var registry = new RouteRegistry(
            rootProvider,
            rootProvider.GetService<IGeneratedRouteHandlerCatalog>()
        );

        foreach (var registration in rootProvider.GetServices<IRouteRegistration>())
        {
            await registration.Register(registry, CancellationToken.None);
        }

        // Throws with every failure at once where anything went wrong, which fails startup rather
        // than leaving the application serving a table that is missing routes nobody noticed.
        provider.Publish(registry.Close());

        return true;
    }
}
