using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunWeb.SUT;

/// <summary>
/// An HTTP application, which happens to be deployed on Cloud Run: the host is the one thing it
/// names, and moving it to another host changes that attribute and a package reference.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunWebApp {
}

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>Ordinary web handlers, with nothing on them that knows where they are hosted.</summary>
public class OrderController {
    [Get("/orders/{id}")]
    public Order Get(string id) => new() { Id = id, Quantity = 7 };

    [Post("/orders")]
    public Order Place(Order order) => order;

    [Delete("/orders/{id}")]
    public void Remove(string id) { }
}
