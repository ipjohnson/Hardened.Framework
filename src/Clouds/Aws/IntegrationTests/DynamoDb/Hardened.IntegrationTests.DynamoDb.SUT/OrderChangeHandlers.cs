using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.DynamoDb.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }

    public decimal Total { get; set; }
}

/// <summary>
/// What a change handler does with a row that moved.
/// </summary>
/// <remarks>
/// Injected rather than static, for the reason the queue fixture records: a static list is one
/// mutable field shared by every test over these handlers.
/// </remarks>
public interface IOrderProjection {
    void Apply(Order order);

    void Raw(IDictionary<string, DynamoDBEvent.AttributeValue>? image);
}

public class OrderChangeHandlers {
    /// <summary>
    /// A row, bound like any other message.
    /// </summary>
    /// <remarks>
    /// The whole claim of the adapter. DynamoDB delivered
    /// <c>{"id":{"S":"a-1"},"total":{"N":"42.5"}}</c> and this handler declares <c>Order</c>, so
    /// nothing in it knows the item arrived type-tagged.
    /// </remarks>
    [Change("orders")]
    public void OnOrderChanged(Order order, IOrderProjection projection) => projection.Apply(order);

    /// <summary>
    /// The same shape of trigger, reached through the raw image instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[NewImage]</c> reaches what a bound parameter cannot say: an absent attribute against a
    /// null one, a set against a list, a number DynamoDB stored more precisely than the handler's
    /// type can hold.
    /// </para>
    /// <para>
    /// Ported from <c>Hardened.Amz.Function.DDB.Runtime</c>, which read it off an injected singleton
    /// holding the record being handled. It comes off the request here, so two records in flight
    /// cannot see each other's image.
    /// </para>
    /// <para>
    /// A second table rather than a second handler on <c>orders</c>: one table is one route, and two
    /// handlers on <c>CHANGE /orders</c> would be a duplicate the router refuses.
    /// </para>
    /// <para>
    /// <b>Both, not just the image.</b> The row is what the handler is about and the image is the
    /// detail it occasionally needs, which is how someone would actually write this. It is also what
    /// the test façade requires: the generated method takes the payload type the handler binds, so a
    /// handler binding only an image is sent nothing and cannot be given one.
    /// </para>
    /// </remarks>
    [Change("audit")]
    public void OnAuditChanged(
        Order order,
        [NewImage] IDictionary<string, DynamoDBEvent.AttributeValue> image,
        IOrderProjection projection) => projection.Raw(image);
}
