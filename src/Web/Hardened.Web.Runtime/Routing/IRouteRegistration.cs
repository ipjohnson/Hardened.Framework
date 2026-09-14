namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Registers routes whose paths the application computes.
/// </summary>
/// <remarks>
/// <para>
/// Implement this, register it in the container, and it runs once during startup. What it
/// registers is a path and a handler the compiler already generated: the handler, its binder, its
/// parameter list and everything a filter reads come from the attribute route it was declared
/// with, and the path is the only part that comes from run time.
/// </para>
/// <code>
/// public class TenantRoutes(ITenantCatalog catalog) : IRouteRegistration
/// {
///     public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
///     {
///         foreach (var tenant in await catalog.Active(cancellationToken))
///         {
///             routes.Get($"/{tenant.Slug}/orders/{{id:int}}", typeof(OrderController), nameof(OrderController.Get));
///         }
///     }
/// }
/// </code>
/// <para>
/// Dependencies arrive through the constructor, which is what every other Hardened service does.
/// <see cref="IRouteRegistry.ServiceProvider"/> is there for genuinely late resolution and nothing
/// else.
/// </para>
/// <para>
/// <b>This runs on the init path.</b> On Lambda that is every cold start, so whatever I/O it does is
/// paid there. It is opt-in, so the cost is one an application chooses knowingly.
/// </para>
/// </remarks>
public interface IRouteRegistration
{
    ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken);
}
