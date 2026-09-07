namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes messages from a queue to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// SQS on AWS, Service Bus on Azure, Pub/Sub on Google. The handler names the queue and nothing
/// else; which adapter delivers to it is decided by the runtime package the project references,
/// through the <c>HardenedQueueModule</c> build property that package declares.
/// </para>
/// <para>
/// That is the same arrangement <c>[Get]</c> already has, and the reason this attribute lives here
/// rather than beside an adapter. A trigger attribute owned by a cloud package would put that
/// cloud's name in the handler, and moving the handler would mean rewriting it.
/// </para>
/// <para>
/// Routes as <c>QUEUE /orders-new</c> through the same table as <c>GET /orders/{id}</c>, because
/// the method slot on a request was always a string.
/// </para>
/// </remarks>
public class QueueAttribute : Attribute {
    public QueueAttribute(string name) {
        Name = name;
    }

    /// <summary>
    /// The queue's own name, not an ARN or a URL.
    /// </summary>
    /// <remarks>
    /// A name is the only part of a queue's identity that survives an account, a region and a
    /// provider, and it is what the adapter reads back off the delivered event to route on. The
    /// full address is the deployment's business.
    /// </remarks>
    public string Name { get; }
}
