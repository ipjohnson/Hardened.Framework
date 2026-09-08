using System.Text.Json;

namespace Hardened.IntegrationTests.AzureQueue.SUT;

/// <summary>
/// An order store that reports every call on the process's output.
/// </summary>
/// <remarks>
/// <para>
/// One line per call, the marker the container harness reads (<c>ObservationMarker.Prefix</c> in
/// <c>Hardened.Functions.Testing.Containers</c>, written here as a literal so this executable
/// depends on no test package) and then a single-line JSON object. The worker's output is captured
/// by the Functions host and printed with its own logs, and the harness finds the marker wherever
/// on a line it lands.
/// </para>
/// <para>
/// A negative quantity is refused, after the line is printed. There is no other way for a test
/// outside the process to make the handler fail, and a failed handler is half of what the host tier
/// has to show: that the invocation is reported failed, the batch is abandoned and the queue
/// delivers the message again - which the second line for the same order id is the evidence of.
/// </para>
/// </remarks>
public sealed class ObservedOrderStore : IOrderStore {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Place(Order order) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(new { kind = "queue", id = order.Id, quantity = order.Quantity }));
        Console.Out.Flush();

        if (order.Quantity < 0) {
            throw new InvalidOperationException($"refused {order.Id}: the quantity is negative");
        }
    }
}
