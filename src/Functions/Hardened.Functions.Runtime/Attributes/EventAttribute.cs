namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes an event from a message bus to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// EventBridge on AWS, Event Grid on Azure. Two fields identify an event rather than one, because a
/// bus carries events from many publishers: <see cref="Source"/> says who published it and
/// <see cref="DetailType"/> says what happened.
/// </para>
/// <para>
/// Routes as <c>EVENT /com.acme.orders/OrderPlaced</c>. The handler binds its parameter from the
/// event's detail rather than from the envelope, so a change to the envelope AWS owns does not
/// reach a handler's signature.
/// </para>
/// </remarks>
public class EventAttribute : Attribute {
    public EventAttribute(string source, string detailType) {
        Source = source;
        DetailType = detailType;
    }

    /// <summary>Who published the event, such as <c>com.acme.orders</c> or <c>aws.s3</c>.</summary>
    public string Source { get; }

    /// <summary>What happened, such as <c>OrderPlaced</c>.</summary>
    public string DetailType { get; }
}
