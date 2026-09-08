using System.Text.Json;

namespace Hardened.IntegrationTests.AzureStream.SUT;

/// <summary>
/// A click sink that reports every call on the process's output, for the host tier.
/// </summary>
/// <remarks>
/// One line per call, the marker the container harness reads and then a single-line JSON object,
/// the arrangement the queue fixture's <c>ObservedOrderStore</c> documents. A negative count is
/// refused after the line is printed, which is how a test outside the process makes the handler
/// fail and sees the partition replayed.
/// </remarks>
public sealed class ObservedClickSink : IClickSink {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Record(Click click) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(new { kind = "stream", id = click.Id, count = click.Count }));
        Console.Out.Flush();

        if (click.Count < 0) {
            throw new InvalidOperationException($"refused {click.Id}: the count is negative");
        }
    }
}
