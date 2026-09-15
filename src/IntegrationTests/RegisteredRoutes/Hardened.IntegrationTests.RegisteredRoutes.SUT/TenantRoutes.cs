using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
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
/// Something that writes a response itself, which is what a view is here.
/// </summary>
/// <remarks>
/// Hand-written rather than a <c>.cshtml</c>, because what is under test is whether a lambda's
/// <c>[Output&lt;T&gt;]</c> is read at all. It writes the id and not the tenant, so what arrives
/// says which of the two wrote the response.
/// </remarks>
public class OrderCard : IHardenedResponseOutput<Order>
{
    public async Task WriteOutput(IExecutionContext context)
    {
        var order = (Order)context.Response.ResponseValue!;

        context.Response.ContentType = "text/html";

        var bytes = Encoding.UTF8.GetBytes("<p>" + order.Id + "</p>");

        await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
    }
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

            // The same shape registered as a lambda. It closes over the tenant, which is the whole
            // reason the closure has to survive into the handler.
            routes.Get(
                $"/{tenant}/ping/{{id:int}}",
                (int id) => Task.FromResult(new Order(id, tenant))
            );

            // A service from the container and a body, so the lambda form is held to the same
            // binding the controller form gets.
            routes.Post(
                $"/{tenant}/echo",
                (Order body, ITenantContext context) =>
                    Task.FromResult(new Order(body.Id, tenant + ":" + context.Current))
            );

            routes.Map(
                "DELETE",
                $"/{tenant}/orders/{{id:int}}",
                (int id) => Task.FromResult(new Order(id, tenant))
            );

            // A media type declared on the lambda. It reached the handler's metadata array and
            // nothing read it there, so the operation published nothing about what it answers and
            // the wire answered a JSON string whatever was asked for.
            routes.Get(
                $"/{tenant}/label/{{id:int}}",
                [Produces("text/plain")]
                (int id) => id + ":" + tenant
            );

            // A view named on the lambda. Ignored the same way, and the model went out as JSON in
            // the view's place - which is the disclosure the attribute exists to prevent.
            routes.Get(
                $"/{tenant}/card/{{id:int}}",
                [Output<OrderCard>]
                (int id) => new Order(id, tenant)
            );
        }

        return default;
    }
}
