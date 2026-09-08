using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunInvoke.SUT;

/// <summary>
/// A directly invoked service. No invoke module attribute: <c>[HardenedFunction]</c> on the
/// handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunInvokeApp {
}

/// <summary>
/// A payload with fields named the way AWS names its own. On Cloud Run nothing inspects a
/// caller's payload at all - the route says it is an invocation - so these are ordinary fields.
/// </summary>
public class OrderRequest {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }

    public List<string> Records { get; set; } = [];

    public string RequestContext { get; set; } = "";
}

public class OrderReceipt {
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";
}

/// <summary>What the handler did with a request; injected so each test sees only its own.</summary>
public interface IOrderLog {
    void Placed(OrderRequest request);
}

public class PlaceOrder {
    /// <summary>
    /// Unnamed, which is what makes it answer whatever operation the URL names: a service hosting
    /// one operation has no name worth matching and the generator emits no switch at all.
    /// </summary>
    [HardenedFunction]
    public OrderReceipt Handle(OrderRequest request, IOrderLog log) {
        log.Placed(request);

        return new OrderReceipt { Id = request.Id, Status = "placed" };
    }
}

/// <summary>An order log that reports every call on the process's output, for the container tier.</summary>
public sealed class ObservedOrderLog : IOrderLog {
    public void Placed(OrderRequest request) {
        Console.Out.WriteLine("HARDENED-OBSERVED " + System.Text.Json.JsonSerializer.Serialize(
            new { kind = "invoke", id = request.Id, quantity = request.Quantity }));
        Console.Out.Flush();
    }
}
