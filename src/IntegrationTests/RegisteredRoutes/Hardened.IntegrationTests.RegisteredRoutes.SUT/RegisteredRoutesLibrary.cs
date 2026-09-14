using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Hardened.Web.Runtime.Routing;

namespace Hardened.IntegrationTests.RegisteredRoutes.SUT;

/// <summary>
/// An application whose routes are partly written and partly registered at startup.
/// </summary>
/// <remarks>
/// <para>
/// The base path is here to prove one thing: a registered path is composed onto it exactly as an
/// attribute route is. There is one routing system, and a module mounted at <c>/registered</c>
/// serves everything it declares under it however the declaration was written.
/// </para>
/// <para>
/// The entry point registers routes itself, which is the shape that needs no class of its own.
/// Nothing puts an entry point in the container, so the generator registers every
/// <c>IRouteRegistration</c> it finds - see <c>RouteRegistrationSelector</c>.
/// </para>
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[Enable<OpenApiDocumentPublishing>]
[BasePath("/registered")]
public partial class RegisteredRoutesLibrary : IRouteRegistration
{
    public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        routes.Get(
            "/module/orders/{id:int}",
            typeof(TenantController),
            nameof(TenantController.Get)
        );

        return default;
    }
}
