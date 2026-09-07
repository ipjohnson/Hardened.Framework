using System.Text.Json.Serialization;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Aot.Lambda.SUT;

/// <summary>
/// A Lambda function published with Native AOT.
/// </summary>
/// <remarks>
/// <para>
/// No module attribute for SQS, the same as every other queue fixture: <c>[Queue]</c> on the
/// handler is what pulls the adapter in, through the build property the adapter package declares.
/// That indirection is a generator emitting a static field initializer, and this project exists
/// partly to prove ILC keeps it.
/// </para>
/// <para>
/// <c>[AotSerializerModule]</c> puts the source-generated serializers ahead of the reflection-based
/// ones, which are annotated <c>RequiresDynamicCode</c>.
/// </para>
/// </remarks>
[HardenedModule]
[AotSerializerModule]
public partial class Application { }

public record Order(string Id, int Quantity);

/// <summary>The metadata the AOT serializers resolve <see cref="Order"/> through.</summary>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Order))]
public partial class AotContext : JsonSerializerContext { }

/// <summary>
/// One handler, exercising the parts of the function path a trimmer could break.
/// </summary>
/// <remarks>
/// The route comes off the delivered event rather than the attribute, the body is bound to a type
/// through a source-generated context, and the answer is written by the adapter - so a native
/// binary that runs this has proved routing, binding and serialization, not only that ILC produced
/// a file.
/// </remarks>
public class OrderHandlers {
    [Queue("orders-new")]
    public void OnOrder(Order order, IOrderSink sink) => sink.Seen(order);
}

/// <summary>Where the handler's result goes, so the entry point can print it.</summary>
public interface IOrderSink {
    void Seen(Order order);
}

public class OrderSink : IOrderSink {
    public Order? Last { get; private set; }

    public void Seen(Order order) => Last = order;
}
