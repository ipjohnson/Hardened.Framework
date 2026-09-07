using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.Events.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// What the handlers in this application did, in order.
/// </summary>
/// <remarks>
/// Injected rather than a static list, so each test observes only its own invocations and no
/// fixture has to reset anything between them.
/// </remarks>
public interface ITriggerLog {
    void Record(string entry);
}

/// <summary>
/// One handler per trigger, written the way an application would write them.
/// </summary>
public class EventHandlers {
    [Queue("orders-new")]
    public void OnQueued(Order order, ITriggerLog log) => log.Record("queue:" + order.Id);

    [Topic("order-events")]
    public void OnPublished(Order order, ITriggerLog log) => log.Record("topic:" + order.Id);

    /// <summary>
    /// No payload, which is the ordinary shape for a schedule: a scheduled invocation carries an
    /// empty detail, so there is nothing to bind and asking for one would be asking for nothing.
    /// </summary>
    [Timer("nightly-rollup")]
    public void Nightly(ITriggerLog log) => log.Record("timer:nightly-rollup");

    /// <summary>
    /// Bound from the event's detail rather than its envelope, so a change to the envelope AWS owns
    /// does not reach this signature.
    /// </summary>
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnPlaced(Order order, ITriggerLog log) => log.Record("event:" + order.Id);
}
