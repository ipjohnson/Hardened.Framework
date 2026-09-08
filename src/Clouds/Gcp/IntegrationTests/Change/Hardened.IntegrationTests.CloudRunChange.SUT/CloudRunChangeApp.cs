using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunChange.SUT;

/// <summary>
/// The entry point a change feed service is anchored on. No Firestore module attribute:
/// <c>[Change]</c> on a handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunChangeApp {
}

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }

    public decimal Total { get; set; }
}

/// <summary>What a change handler does with a document that moved; injected so each test observes only its own.</summary>
public interface IOrderProjection {
    void Apply(Order order);

    void Previous(Order? previous);
}

public class OrderChangeHandlers {
    /// <summary>
    /// A document, bound like any other message. Firestore delivered typed values and this handler
    /// declares <c>Order</c>, so nothing in it knows the document arrived that way.
    /// </summary>
    [Change("orders")]
    public void OnOrderChanged(Order order, IOrderProjection projection) => projection.Apply(order);

    /// <summary>
    /// The same shape of trigger, also reading the document as it was. A second collection rather
    /// than a second handler on <c>orders</c>: one collection is one route.
    /// </summary>
    [Change("audit")]
    public void OnAuditChanged(Order order, [OldValue] Order? previous, IOrderProjection projection) =>
        projection.Previous(previous);
}
