using System.Diagnostics;
using Hardened.Requests.Runtime.Diagnostics;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Diagnostics;

/// <summary>
/// The two objects the request pipeline reports through, and the one string that reaches them.
/// </summary>
[Collection(DiagnosticsListenerCollection.Name)]
public class HardenedDiagnosticsTests {

    /// <summary>
    /// A published contract, not an implementation detail. This is the literal an application passes
    /// to <c>AddSource</c> and <c>AddMeter</c>, so changing it does not break a build anywhere — it
    /// silently stops collection for everyone who already configured it. Asserting the value is the
    /// only thing that turns that into a failure someone sees.
    /// </summary>
    [Fact]
    public void TheSourceNameIsTheStringApplicationsSubscribeTo() {
        Assert.Equal("Hardened.Requests", HardenedDiagnostics.SourceName);
    }

    /// <summary>
    /// One name for both signals, so an application configures traces and metrics with the same
    /// literal rather than discovering that one of the two was spelled differently.
    /// </summary>
    [Fact]
    public void TheMeterAndTheActivitySourceShareThatName() {
        Assert.Equal(HardenedDiagnostics.SourceName, HardenedDiagnostics.ActivitySource.Name);
        Assert.Equal(HardenedDiagnostics.SourceName, HardenedDiagnostics.Meter.Name);
    }

    /// <summary>
    /// The property the whole design rests on: with nothing listening, starting an activity
    /// allocates nothing and returns null. That is what makes it reasonable for the pipeline to
    /// instrument unconditionally, in the runtime, rather than behind a flag or an opt-in package —
    /// so it is worth an assertion rather than a comment.
    /// </summary>
    [Fact]
    public void NothingIsProducedWhenNothingIsListening() {
        Assert.Null(HardenedDiagnostics.ActivitySource.StartActivity("GET /orders"));
    }

    /// <summary>
    /// And the other half: a listener that subscribes by name gets the activity. No OpenTelemetry
    /// involved — <c>ActivityListener</c> is in the base class library, which is the same reason the
    /// instrumented side needs no package either.
    /// </summary>
    [Fact]
    public void AListenerSubscribedByNameSeesActivities() {
        var started = new List<Activity>();

        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == HardenedDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = started.Add
        };

        ActivitySource.AddActivityListener(listener);

        using var activity = HardenedDiagnostics.ActivitySource.StartActivity("GET /orders");

        Assert.NotNull(activity);
        Assert.Equal("GET /orders", Assert.Single(started).OperationName);
    }
}
