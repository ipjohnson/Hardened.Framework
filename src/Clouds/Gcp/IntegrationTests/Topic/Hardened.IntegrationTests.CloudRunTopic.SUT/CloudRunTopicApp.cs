using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunTopic.SUT;

/// <summary>
/// The entry point a topic service is anchored on. No Pub/Sub module attribute: <c>[Topic]</c> on
/// the handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunTopicApp {
}

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>What a topic handler does with an order; injected so each test observes only its own.</summary>
public interface IOrderStore {
    void Place(Order order);
}

public class OrderHandlers {
    /// <summary>
    /// Fan-out rather than a queue: every subscriber sees every message. On Google the message
    /// arrives through an Eventarc trigger on the topic, whose CloudEvent names the topic, which is
    /// what routes it.
    /// </summary>
    [Topic("order-events")]
    public void OnPublished(Order order, IOrderStore store) => store.Place(order);
}
