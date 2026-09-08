using Hardened.Gcp.CloudRun.Scheduler;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A Cloud Scheduler job's request: the timer's name in the URL, cross-checked against the job.
/// </summary>
public class SchedulerEnvelopeTests {
    private static readonly SchedulerEnvelope Envelope = new(SchedulerEnvelope.DefaultPrefix);

    [Fact]
    public void TheNameInTheUrlIsTheRoute() {
        var delivery = Deliveries.Post("/_triggers/timer/nightly-rollup",
            (SchedulerEnvelope.MarkerHeader, "true"),
            (SchedulerEnvelope.ScheduleTimeHeader, "2026-09-07T02:00:00Z"));

        var request = Deliveries.Unwrap(Envelope, delivery, "")!;

        Assert.Equal("TIMER", request.Method);
        Assert.Equal("/nightly-rollup", request.Path);
        Assert.Equal("2026-09-07T02:00:00Z", request.Headers[SchedulerEnvelope.ScheduleTimeHeader].ToString());
        Assert.Equal(0, request.Body.Length);
    }

    /// <summary>A job configured with a body reaches a handler that binds one.</summary>
    [Fact]
    public void TheBodyIsHandedOnAsItArrived() {
        var request = Deliveries.Unwrap(Envelope, Deliveries.Post("/_triggers/timer/nightly-rollup"), "{\"scope\":\"all\"}")!;

        Assert.Equal("{\"scope\":\"all\"}", Deliveries.Text(request.Body));
    }

    [Theory]
    [InlineData("nightly-rollup")]
    [InlineData("projects/p/locations/europe-west1/jobs/nightly-rollup")]
    public void AJobNameThatAgreesIsAccepted(string job) {
        var delivery = Deliveries.Post("/_triggers/timer/nightly-rollup", (SchedulerEnvelope.JobNameHeader, job));

        Assert.NotNull(Deliveries.Unwrap(Envelope, delivery, ""));
    }

    /// <summary>A job posting to another timer's URL is a wiring error, refused rather than run under the wrong name.</summary>
    [Fact]
    public void AJobNameThatDisagreesWithTheUrlIsRefused() {
        var delivery = Deliveries.Post("/_triggers/timer/nightly-rollup", (SchedulerEnvelope.JobNameHeader, "hourly-sweep"));

        var failure = Assert.Throws<InvalidOperationException>(() => Deliveries.Unwrap(Envelope, delivery, ""));

        Assert.Contains("hourly-sweep", failure.Message);
        Assert.Contains("nightly-rollup", failure.Message);
    }

    /// <summary>A job may be a GET; the path is what says it is a timer.</summary>
    [Theory]
    [InlineData("GET", "/_triggers/timer/nightly", true)]
    [InlineData("POST", "/_triggers/timer/nightly/", true)]
    [InlineData("POST", "/_triggers/timer/", false)]
    [InlineData("POST", "/_triggers/timer", false)]
    [InlineData("POST", "/orders", false)]
    public void OnlyThePrefixedPathIsRecognised(string method, string path, bool recognised) {
        Assert.Equal(recognised, Envelope.Recognises(Deliveries.Request(method, path, null)));
    }

    [Theory]
    [InlineData("jobs", "/jobs/")]
    [InlineData("/jobs", "/jobs/")]
    [InlineData("/jobs/", "/jobs/")]
    public void ThePrefixIsRootedAndEndsInASlash(string configured, string prefix) {
        Assert.Equal(prefix, new SchedulerEnvelope(configured).Prefix);
    }

    [Fact]
    public void AConfiguredPrefixIsWhatIsRead() {
        var envelope = new SchedulerEnvelope("/jobs");

        var request = Deliveries.Unwrap(envelope, Deliveries.Post("/jobs/nightly"), "")!;

        Assert.Equal("/nightly", request.Path);
        Assert.False(envelope.Recognises(Deliveries.Post("/_triggers/timer/nightly")));
    }
}
