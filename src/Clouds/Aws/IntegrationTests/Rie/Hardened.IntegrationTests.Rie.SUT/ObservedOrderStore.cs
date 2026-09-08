using System.Text.Json;
using Hardened.IntegrationTests.Sqs.SUT;

namespace Hardened.IntegrationTests.Rie.SUT;

/// <summary>
/// An order store that reports every call on the process's output.
/// </summary>
/// <remarks>
/// <para>
/// One line per call, the marker the container harness reads (<c>ObservationMarker.Prefix</c> in
/// <c>Hardened.Functions.Testing.Containers</c>, written here as a literal so this executable
/// depends on no test package) and then a single-line JSON object.
/// </para>
/// <para>
/// A negative quantity is refused. There is no other way for a test outside the process to make
/// the handler fail, and a failed handler is half of what the container tier has to show: that
/// the invocation is reported failed to the runtime, so the queue redelivers.
/// </para>
/// </remarks>
public sealed class ObservedOrderStore : IOrderStore {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Place(Order order) {
        if (order.Quantity < 0) {
            throw new InvalidOperationException($"refused {order.Id}: the quantity is negative");
        }

        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(new { kind = "queue", id = order.Id, quantity = order.Quantity }));
        Console.Out.Flush();
    }
}
