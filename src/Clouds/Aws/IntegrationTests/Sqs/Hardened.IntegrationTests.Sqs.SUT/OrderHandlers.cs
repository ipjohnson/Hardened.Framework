using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.Sqs.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// What a queue handler does with an order.
/// </summary>
/// <remarks>
/// The seam a test observes through. The handler used to write to a static list, which meant every
/// fixture over these handlers shared one mutable field: two test classes running in parallel reset
/// each other, and a helper that reset before arranging silently discarded what the test had just
/// set up. Both happened. An injected dependency gives each test its own.
/// </remarks>
public interface IOrderStore {
    void Place(Order order);
}

public class OrderHandlers {
    /// <remarks>
    /// The store arrives as a parameter rather than through a constructor, which the binder
    /// resolves from the request's services - so the handler stays a plain method and the test
    /// still chooses the implementation.
    /// </remarks>
    [Queue("orders-new")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}
