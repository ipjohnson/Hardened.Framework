using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// A handler that throws fails the invocation, on the sources that deliver one message and never
/// fan out.
/// </summary>
/// <remarks>
/// Found on 2026-09-08 from the Azure line, whose worker met the same shape: the invoke filter
/// records a handler's exception on the response rather than throwing it, the batch filter reads
/// that value back for a batched source and rethrows, and nothing read it for an unbatched one.
/// A scheduled rule or a bus event whose handler threw was therefore reported to Lambda as
/// handled, which is the one outcome an event source cannot recover from: no retry, no dead
/// letter, no failed-invocation metric. The schedule stands for the whole unbatched family here
/// because a bus event has no test façade yet; the fix is in the host and covers both.
/// </remarks>
public class FailedHandlerTests {

    [HardenedTest]
    public async Task AFailedTimerHandlerFailsTheInvocation(
        EventsTestApp.Timers timers, [Mock] ITriggerLog log) {
        log.When(one => one.Record("timer:nightly-rollup"))
            .Do(_ => throw new InvalidOperationException("the rollup refused"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => timers.NightlyRollup());

        Assert.Equal("the rollup refused", failure.Message);
    }
}
