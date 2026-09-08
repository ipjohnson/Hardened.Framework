using DependencyModules.Testing.Attributes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunTimer.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunTimer.SUT.Tests;

/// <summary>
/// A scheduled service, whole: the trigger attribute, the generator, the module the build
/// property named, the Scheduler envelope, the front door and the one dispatch. The same test the
/// schedule half of the AWS events fixture holds, on the pipeline host.
/// </summary>
public class TimerTests {

    /// <summary>
    /// A schedule routes on the name in the job's target URL, and it carries no payload, so the
    /// handler takes none.
    /// </summary>
    [HardenedTest]
    public async Task AScheduledInvocationReachesTheTimerHandler(CloudRunTimerApp.Timers timers, [Mock] ITriggerLog log) {
        await timers.NightlyRollup();

        log.Received().Record("timer:nightly-rollup");
    }

    /// <summary>A job posting to another timer's URL is refused: the wiring is wrong, and Scheduler sees the failure.</summary>
    [HardenedTest]
    public async Task AJobPostingToAnotherTimersUrlIsRefused(ITestWebApp app, [Mock] ITriggerLog log) {
        var response = await app.Post("", "/_triggers/timer/nightly-rollup",
            request => request.Headers["X-CloudScheduler-JobName"] = "hourly-sweep");

        Assert.Equal(500, response.StatusCode);

        log.DidNotReceive().Record(Arg.Any<string>());
    }
}

/// <summary>The same schedule over a Kestrel socket.</summary>
[KestrelRuntime]
public class TimerOverASocketTests {

    [HardenedTest]
    public async Task AScheduledInvocationReachesTheTimerHandler(CloudRunTimerApp.Timers timers, [Mock] ITriggerLog log) {
        await timers.NightlyRollup();

        log.Received().Record("timer:nightly-rollup");
    }
}

/// <summary>The same handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineTimerTests {

    [HardenedTest]
    public async Task AScheduledInvocationReachesTheTimerHandlerThroughThePipeline(CloudRunTimerApp.Timers timers, [Mock] ITriggerLog log) {
        await timers.NightlyRollup();

        log.Received().Record("timer:nightly-rollup");
    }
}
