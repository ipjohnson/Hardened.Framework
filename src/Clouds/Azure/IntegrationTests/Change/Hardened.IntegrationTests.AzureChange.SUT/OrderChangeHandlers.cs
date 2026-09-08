using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureChange.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }

    public decimal Total { get; set; }
}

/// <summary>
/// What a change handler does with a document that moved.
/// </summary>
/// <remarks>
/// Injected rather than static, for the reason the queue fixture records.
/// </remarks>
public interface IOrderProjection {
    void Apply(Order order);

    void Audit(Order order);
}

public class OrderChangeHandlers {
    /// <summary>
    /// A document, bound like any other message.
    /// </summary>
    /// <remarks>
    /// The whole claim of the adapter. The feed delivered the document with <c>_lsn</c>, <c>_ts</c>
    /// and <c>_etag</c> stamped on it and this handler declares <c>Order</c>, so nothing in it
    /// knows the document arrived with system properties beside its own.
    /// </remarks>
    [Change("orders")]
    public void OnOrderChanged(Order order, IOrderProjection projection) => projection.Apply(order);

    /// <summary>
    /// A second container rather than a second handler on <c>orders</c>: one container is one
    /// route, and two handlers on <c>CHANGE /orders</c> would be a duplicate the router refuses.
    /// </summary>
    /// <remarks>
    /// <b>No raw image, and this is where the stores differ.</b> The DynamoDB twin reaches the
    /// row's type-tagged form through <c>[NewImage]</c> and its previous version through
    /// <c>[OldImage]</c>. The change feed carries neither: a document is plain JSON, which the
    /// bound parameter already is, and what a document was before the change is not on the feed
    /// at all. The Cosmos adapter's documentation records the divergence; this handler binds the
    /// document and nothing else, because there is nothing else.
    /// </remarks>
    [Change("audit")]
    public void OnAuditChanged(Order order, IOrderProjection projection) => projection.Audit(order);
}
