using System.Text.Json;
using Hardened.IntegrationTests.Events.SUT;

namespace Hardened.IntegrationTests.RieEvents.SUT;

/// <summary>
/// The fixture's log, reported on the process's output for the container harness to read.
/// </summary>
public sealed class ObservedTriggerLog : ITriggerLog {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Record(string entry) {
        Console.Out.WriteLine(Marker + JsonSerializer.Serialize(new { kind = "trigger", entry }));
        Console.Out.Flush();
    }
}
