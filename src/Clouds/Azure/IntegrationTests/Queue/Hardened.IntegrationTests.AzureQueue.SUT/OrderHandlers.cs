using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureQueue.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// What a queue handler does with an order.
/// </summary>
/// <remarks>
/// The seam a test observes through: a <c>[Mock]</c> in the pipeline and envelope tests, and the
/// store that prints to the container's output in the host tier. An injected dependency rather than
/// a static, for the reason the SQS fixture gives - a static shared by every test class in the
/// assembly is what made two of them reset each other.
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
