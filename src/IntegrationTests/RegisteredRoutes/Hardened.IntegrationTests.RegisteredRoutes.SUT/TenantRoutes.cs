using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.Routing;

namespace Hardened.IntegrationTests.RegisteredRoutes.SUT;

/// <summary>
/// What a tenant list would be in a real application.
/// </summary>
public interface ITenantCatalog
{
    IReadOnlyList<string> Active();
}

/// <inheritdoc />
[SingletonService]
public class TenantCatalog : ITenantCatalog
{
    public IReadOnlyList<string> Active() => new[] { "acme", "globex" };
}

/// <summary>Which tenant the request addressed, read from the bound path token.</summary>
public interface ITenantContext
{
    string Current { get; }
}

/// <inheritdoc />
[SingletonService]
public class TenantContext : ITenantContext
{
    public string Current => "any";
}

/// <summary>
/// One route per tenant, from a list that does not exist until the application runs.
/// </summary>
/// <remarks>
/// No <c>[SingletonService]</c>. Implementing the interface is the declaration, and the generator
/// registers what it finds.
/// </remarks>
public class TenantRoutes : IRouteRegistration
{
    private readonly ITenantCatalog _catalog;

    public TenantRoutes(ITenantCatalog catalog)
    {
        _catalog = catalog;
    }

    public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        foreach (var tenant in _catalog.Active())
        {
            routes
                .Get(
                    $"/{tenant}/orders/{{id:int}}",
                    typeof(TenantController),
                    nameof(TenantController.Get)
                )
                .Post($"/{tenant}/orders", typeof(TenantController), nameof(TenantController.Place))
                .Get(
                    $"/{tenant}/files/{{*path}}",
                    typeof(TenantController),
                    nameof(TenantController.File)
                );
        }

        return default;
    }
}
