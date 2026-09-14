using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.RegisteredRoutes.SUT;

public record Order(int Id, string Tenant);

/// <summary>
/// Ordinary handlers, written the ordinary way.
/// </summary>
/// <remarks>
/// Nothing here knows it is going to be served at more than one path. The generator reads these
/// declarations, emits a handler for each, and the registration below points computed paths at the
/// same classes.
/// </remarks>
public class TenantController
{
    [Get("/orders/{id:int}")]
    public Task<Order> Get(int id, ITenantContext tenant) =>
        Task.FromResult(new Order(id, tenant.Current));

    [Post("/orders")]
    public Task<Order> Place(Order body) => Task.FromResult(body);

    [Get("/files/{*path}")]
    public Task<string> File(string path) => Task.FromResult(path);
}
