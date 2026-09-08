using System.Text.Json;
using Hardened.IntegrationTests.Invoke.SUT;

namespace Hardened.IntegrationTests.RieInvoke.SUT;

/// <summary>
/// The fixture's order log, reported on the process's output for the container harness to read.
/// </summary>
public sealed class ObservedOrderLog : IOrderLog {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Placed(OrderRequest request) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(new { kind = "invoke", id = request.Id, quantity = request.Quantity }));
        Console.Out.Flush();
    }
}
