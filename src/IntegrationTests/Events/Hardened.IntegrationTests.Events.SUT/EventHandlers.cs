using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.Events.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// One handler per trigger, written the way an application would write them.
/// </summary>
public class EventHandlers {
    /// <summary>What ran, in order, as "trigger:detail" so a test can see which one was reached.</summary>
    public static readonly List<string> Ran = [];

    public static void Reset() => Ran.Clear();

    [Queue("orders-new")]
    public void OnQueued(Order order) => Ran.Add("queue:" + order.Id);

    [Topic("order-events")]
    public void OnPublished(Order order) => Ran.Add("topic:" + order.Id);

    /// <summary>
    /// No parameter, which is the ordinary shape for a schedule: a scheduled invocation carries an
    /// empty detail, so there is nothing to bind and asking for one would be asking for nothing.
    /// </summary>
    [Timer("nightly-rollup")]
    public void Nightly() => Ran.Add("timer:nightly-rollup");

    /// <summary>
    /// Bound from the event's detail rather than its envelope, so a change to the envelope AWS owns
    /// does not reach this signature.
    /// </summary>
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnPlaced(Order order) => Ran.Add("event:" + order.Id);
}
