using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunEvent.SUT;

/// <summary>
/// The entry point an event service is anchored on. No Eventarc module attribute: <c>[Event]</c>
/// on the handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunEventApp {
}

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>What the handlers in this application did; injected so each test observes only its own.</summary>
public interface ITriggerLog {
    void Record(string entry);
}

public class EventHandlers {
    /// <summary>
    /// Bound from the event's data rather than its envelope, so a change to the CloudEvent
    /// attributes does not reach this signature. The source and type are the two halves of the
    /// route, exactly as the EventBridge adapter routes a bus event.
    /// </summary>
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnPlaced(Order order, ITriggerLog log) => log.Record("event:" + order.Id);
}

/// <summary>A log that reports every entry on the process's output, for the container tier.</summary>
public sealed class ObservedTriggerLog : ITriggerLog {
    public void Record(string entry) {
        Console.Out.WriteLine("HARDENED-OBSERVED " + System.Text.Json.JsonSerializer.Serialize(new { kind = "event", entry }));
        Console.Out.Flush();
    }
}
