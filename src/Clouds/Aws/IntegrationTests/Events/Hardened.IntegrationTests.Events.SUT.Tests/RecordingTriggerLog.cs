using Hardened.IntegrationTests.Events.SUT;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>What ran, in order, for one test.</summary>
public sealed class RecordingTriggerLog : ITriggerLog {
    public List<string> Entries { get; } = [];

    public void Record(string entry) => Entries.Add(entry);
}
