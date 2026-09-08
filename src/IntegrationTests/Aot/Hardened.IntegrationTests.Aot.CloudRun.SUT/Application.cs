using System.Text.Json.Serialization;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Aot.CloudRun.SUT;

/// <summary>
/// A Cloud Run service published with Native AOT.
/// </summary>
/// <remarks>
/// <para>
/// The host is the one thing named here, the way a web application names <c>[KestrelRuntime]</c>.
/// No module attribute for Pub/Sub or Firestore, the same as every other fixture: the trigger on
/// the handler is what pulls the adapter in, through the build property the adapter package
/// declares, and that indirection is a generator emitting a static field initializer ILC has to
/// keep. The composite dispatch the host installs is another one.
/// </para>
/// <para>
/// <c>[AotSerializerModule]</c> puts the source-generated serializers ahead of the reflection-based
/// ones, which are annotated <c>RequiresDynamicCode</c>.
/// </para>
/// </remarks>
[HardenedModule]
[CloudRunRuntime]
[AotSerializerModule]
public partial class Application { }

public record Order(string Id, int Quantity);

/// <summary>The metadata the AOT serializers resolve <see cref="Order"/> through.</summary>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Order))]
public partial class AotContext : JsonSerializerContext { }

/// <summary>
/// One handler per path a Cloud Run service serves, exercising the parts a trimmer could break.
/// </summary>
/// <remarks>
/// The push is recognised by the front door, its base64 data becomes the body, the route comes off
/// the subscription's name rather than the attribute, and the body is bound through a
/// source-generated context. The Firestore event is protobuf, decoded by the one Google dependency
/// in the line, projected to JSON, and its old value bound through the same context; a native
/// binary that answers it has proved <c>Google.Protobuf</c> survives ILC, which the analyzers
/// cannot say. The web route goes through the same socket and the same dispatch to the other path.
/// </remarks>
public class OrderHandlers {
    [Queue("orders-new")]
    public void OnOrder(Order order, IOrderSink sink) => sink.Seen(order);

    [Change("orders")]
    public void OnOrderChanged(Order order, [OldValue] Order? previous, IOrderSink sink) =>
        sink.Changed(order, previous);

    [Get("/health")]
    public string Health() => "ok";
}

/// <summary>Where the handlers' results go, so the probe can read them.</summary>
public interface IOrderSink {
    void Seen(Order order);

    void Changed(Order order, Order? previous);
}

/// <summary>
/// Prints rather than stores: the service runs until Cloud Run ends it, so the entry point has no
/// moment after the handler to print anything itself.
/// </summary>
public class OrderSink : IOrderSink {
    public void Seen(Order order) => Console.WriteLine($"HANDLED {order.Id} x{order.Quantity}");

    public void Changed(Order order, Order? previous) =>
        Console.WriteLine($"CHANGED {order.Id} x{order.Quantity} from {(previous is null ? "nothing" : "x" + previous.Quantity)}");
}
