using System.Text.Json.Serialization;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Aot.AzureFunctions.SUT;

/// <summary>
/// An Azure Functions worker published with Native AOT.
/// </summary>
/// <remarks>
/// <para>
/// No module attribute for Service Bus or for the HTTP trigger, the same as every other fixture:
/// the trigger on the handler and the verb on the route are what pull the adapters in, through
/// the build properties the adapter packages declare, and each of those is a generator emitting a
/// static field initializer ILC has to keep. <c>[HardenedWebModule]</c> brings the routing table
/// the HTTP function dispatches through.
/// </para>
/// <para>
/// <c>[AotSerializerModule]</c> puts the source-generated serializers ahead of the reflection-based
/// ones, which are annotated <c>RequiresDynamicCode</c>.
/// </para>
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[AotSerializerModule]
public partial class Application { }

public record Order(string Id, int Quantity);

/// <summary>The metadata the AOT serializers resolve <see cref="Order"/> through.</summary>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Order))]
public partial class AotContext : JsonSerializerContext { }

/// <summary>
/// One handler per dispatch, exercising the parts a trimmer could break.
/// </summary>
/// <remarks>
/// The queue message arrives as the extension's own received message, bound through a
/// source-generated context after the batch filter forked it; the web route goes through the
/// catch-all HTTP function, the host's route prefix taken off, into the web table. Both are
/// indexed by the host from the generated provider, which is the first thing the probe checks.
/// </remarks>
public class OrderHandlers {
    [Queue("orders-new")]
    public void OnOrder(Order order, IOrderSink sink) => sink.Seen(order);

    [Get("/health")]
    public string Health() => "ok";
}

/// <summary>Where the handler's result goes, so the probe can read it off the host's log.</summary>
public interface IOrderSink {
    void Seen(Order order);
}

/// <summary>
/// Prints rather than stores: the worker runs until the host ends it, so the entry point has no
/// moment after the handler to print anything itself, and the host relays the worker's output.
/// </summary>
public class OrderSink : IOrderSink {
    public void Seen(Order order) => Console.WriteLine($"HANDLED {order.Id} x{order.Quantity}");
}
