using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.Gcp.Functions.Runtime.Tests;

/// <summary>
/// A service with a web route and a queue handler, served on both Google deployment models.
/// </summary>
/// <remarks>
/// <c>[CloudRunRuntime]</c> and no attribute of its own, which is the point of the suite: the
/// Cloud Functions host is an entry point, not a module, so the application a function deploys is
/// the application a container deploys, compiled once. A second host attribute here would be the
/// one thing a benchmark comparing the two cannot have.
/// </remarks>
[HardenedModule]
[CloudRunRuntime]
public partial class FunctionsApp;

public class Order
{
    public string Id { get; set; } = "";
}

public interface IOrderStore
{
    void Place(Order order);
}

public class OrderHandlers
{
    [Queue("orders")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}

public record Pong(string Answer);

public class PingController
{
    [Get("/ping")]
    public Pong Ping() => new("pong");
}
