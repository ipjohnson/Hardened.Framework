using Hardened.Functions.Runtime.Attributes;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Dispatch;

/// <summary>
/// A service with a web route and a queue handler: the shape a Lambda function refuses and a
/// Cloud Run service serves over one socket.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class MixedApp {
}

public class Order {
    public string Id { get; set; } = "";
}

public interface IOrderStore {
    void Place(Order order);
}

public class OrderHandlers {
    [Queue("orders")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}

public record Pong(string Answer);

public class PingController {
    [Get("/ping")]
    public Pong Ping() => new("pong");
}
