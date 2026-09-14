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
    /// <summary>One tenant's order.</summary>
    /// <remarks>
    /// The punctuation here is the fixture. This comment reaches a C# string literal in the
    /// handler catalog and the document the application splices a registered path into, so it
    /// carries a double quote ("null") and a backslash (C:\orders) to hold both escapers open.
    /// 0.36.0-rc1000 escaped the quote and not the backslash: the quote ended the literal and
    /// stopped the compilation, and the backslash served a document System.Text.Json refused to
    /// parse.
    /// </remarks>
    [Get("/orders/{id:int}")]
    public Task<Order> Get(int id, ITenantContext tenant) =>
        Task.FromResult(new Order(id, tenant.Current));

    [Post("/orders")]
    public Task<Order> Place(Order body) => Task.FromResult(body);

    [Get("/files/{*path}")]
    public Task<string> File(string path) => Task.FromResult(path);
}
