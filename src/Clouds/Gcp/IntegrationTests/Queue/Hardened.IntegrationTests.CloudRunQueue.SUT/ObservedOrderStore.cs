using System.Text.Json;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT;

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
/// Two orders are treated specially, because a test outside the process has no other way to
/// shape what the handler does. A negative quantity is refused, after its line is printed with
/// <c>refused</c> set, which is how the container tier shows that a failed handler is a negative
/// acknowledgement and counts the emulator's redeliveries. An id starting with <c>slow-</c> is
/// held for a few seconds before it is placed, which is how the tier sends <c>SIGTERM</c> while a
/// request is in flight and reads whether it was allowed to finish; that line is printed only
/// once the wait is over, so it is the evidence.
/// </para>
/// </remarks>
public sealed class ObservedOrderStore : IOrderStore {
    private const string Marker = "HARDENED-OBSERVED ";

    /// <summary>What a <c>slow-</c> order waits, comfortably longer than a stop takes to arrive.</summary>
    public static readonly TimeSpan SlowOrder = TimeSpan.FromSeconds(3);

    public void Place(Order order) {
        if (order.Quantity < 0) {
            Print(order, refused: true);

            throw new InvalidOperationException($"refused {order.Id}: the quantity is negative");
        }

        if (order.Id.StartsWith("slow-", StringComparison.Ordinal)) {
            Thread.Sleep(SlowOrder);
        }

        Print(order, refused: false);
    }

    private static void Print(Order order, bool refused) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(
                new { kind = "queue", id = order.Id, quantity = order.Quantity, refused = refused ? "true" : "false" }));
        Console.Out.Flush();
    }
}
