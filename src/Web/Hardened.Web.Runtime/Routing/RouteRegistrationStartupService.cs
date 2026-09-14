using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;
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

        var catalog = rootProvider.GetService<IGeneratedRouteHandlerCatalog>();
        var registry = new RouteRegistry(rootProvider, catalog);

        foreach (var registration in rootProvider.GetServices<IRouteRegistration>())
        {
            await registration.Register(registry, CancellationToken.None);
        }

        // Throws with every failure at once where anything went wrong, which fails startup rather
        // than leaving the application serving a table that is missing routes nobody noticed.
        provider.Publish(registry.Close());

        Describe(rootProvider, catalog, registry);

        return true;
    }

    /// <summary>
    /// Writes the registered routes into the document the application serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After the table, and only where the table was built: a route that failed to register is in
    /// the document of no application, because a failed registration fails startup.
    /// </para>
    /// <para>
    /// Found by walking the providers rather than resolved by type, because the document provider
    /// is registered as an <c>IWebExecutionRequestHandlerProvider</c> through a factory - there is
    /// no <c>OpenApiDocumentProvider</c> registration to ask for. An application serving several
    /// documents gets them all, which is right: each one describes the same application.
    /// </para>
    /// </remarks>
    private static void Describe(
        IServiceProvider rootProvider,
        IGeneratedRouteHandlerCatalog? catalog,
        RouteRegistry registry
    )
    {
        if (catalog == null || registry.Operations.Count == 0)
        {
            return;
        }

        var document = RegisteredRouteDocument.Splice(
            catalog.DocumentPrefix,
            catalog.DocumentSuffix,
            registry.Operations
        );

        if (document == null)
        {
            return;
        }

        foreach (
            var served in rootProvider
                .GetServices<IWebExecutionRequestHandlerProvider>()
                .OfType<OpenApiDocumentProvider>()
        )
        {
            served.Publish(document);
        }
    }
}
