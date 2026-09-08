using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// What a queue handler does with an order.
/// </summary>
/// <remarks>
/// The seam a test observes through: a substitute in a pipeline or envelope test, and a store that
/// prints one line per call in the container tier, where nothing else crosses the process
/// boundary. An injected dependency rather than a static, so two test classes running in parallel
/// cannot reset each other.
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
    [Queue("orders")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}
