using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunTimer.SUT;

/// <summary>
/// The entry point a scheduled service is anchored on. No Scheduler module attribute: <c>[Timer]</c>
/// on the handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunTimerApp {
}

/// <summary>What the handlers in this application did, in order; injected so each test observes only its own.</summary>
public interface ITriggerLog {
    void Record(string entry);
}

public class ScheduleHandlers {
    /// <summary>
    /// No payload, which is the ordinary shape for a schedule: a Scheduler job carries no body
    /// unless one was configured, so there is nothing to bind and asking for one would be asking
    /// for nothing.
    /// </summary>
    [Timer("nightly-rollup")]
    public void Nightly(ITriggerLog log) => log.Record("timer:nightly-rollup");
}

/// <summary>
/// A log that reports every entry on the process's output, for the container tier: the marker the
/// harness reads, written as a literal so this executable depends on no test package.
/// </summary>
public sealed class ObservedTriggerLog : ITriggerLog {
    public void Record(string entry) {
        Console.Out.WriteLine("HARDENED-OBSERVED " + System.Text.Json.JsonSerializer.Serialize(new { kind = "timer", entry }));
        Console.Out.Flush();
    }
}
