using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.ApiGateway.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// Ordinary web handlers, with nothing on them that knows where they are hosted.
/// </summary>
public class OrderController {
    [Get("/orders/{id}")]
    public Order Get(string id) => new() { Id = id, Quantity = 7 };

    [Post("/orders")]
    public Order Place(Order order) => order;

    [Delete("/orders/{id}")]
    public void Remove(string id) { }
}
