using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// What an invocation keeps from the one before it, through the real Lambda envelope.
/// </summary>
/// <remarks>
/// <para>
/// The case the whole boundary exists for. Two queues deployed as two functions are two processes,
/// and a sandbox is not promised between invocations of one function either, so a handler that
/// leaned on what a previous invocation left in a singleton fails intermittently in production and
/// never in a test that reused one container.
/// </para>
/// <para>
/// These go through <c>[LambdaTesting]</c>, so each send builds the envelope its source sends and
/// runs it through <c>LambdaInvocationHandler</c> on a container of its own - which also means a
/// fresh handler, installing dispatch for the first time, exactly as a cold environment does.
/// </para>
/// </remarks>
public class ContainerIsolationTests {

    /// <summary>
    /// Every send gets its own container, so a singleton never carries anything forward.
    /// </summary>
    /// <remarks>
    /// Asserted through a mock rather than by reading the counter afterwards, because the counter
    /// the test could hold belongs to the container the test was resolved from and not to any of
    /// the ones the sends ran on. The mock is pinned, so what every container did is visible here,
    /// and that is the arrangement <c>[Mock]</c> declaring itself shared exists to give.
    /// </remarks>
    [HardenedTest]
    public async Task EachInvocationRunsOnItsOwnContainer(
        EventsTestApp.Queues queues, [Mock] ITriggerLog log) {
        await queues.OrdersNew(new Order { Id = "q-1" });
        await queues.OrdersNew(new Order { Id = "q-2" });

        Received.InOrder(() => {
            log.Record("queue:q-1");
            log.Record("queue:q-2");
        });
    }

    /// <summary>
    /// Two queues are two functions, so nothing one leaves behind is there for the other.
    /// </summary>
    [HardenedTest]
    public async Task TwoSourcesDoNotShareAContainer(
        EventsTestApp.Queues queues, EventsTestApp.Topics topics, [Mock] ITriggerLog log) {
        await queues.OrdersNew(new Order { Id = "q-1" });
        await topics.OrderEvents(new Order { Id = "t-1" });

        Received.InOrder(() => {
            log.Record("queue:q-1");
            log.Record("topic:t-1");
        });
    }

    /// <summary>
    /// A batch is one invocation, so three messages in one send share the container the other two
    /// sends do not.
    /// </summary>
    /// <remarks>
    /// The distinction the façade's <c>params</c> signature makes: three messages in one call arrive
    /// as one SQS event carrying three records, and the fan-out to a handler call per message is the
    /// batch filter's work inside that single invocation. Three separate calls would be three
    /// invocations and three containers.
    /// </remarks>
    [HardenedTest]
    public async Task ABatchIsOneInvocationAndOneContainer(
        EventsTestApp.Queues queues, [Mock] ITriggerLog log) {
        await queues.OrdersNew(
            new Order { Id = "b-1" }, new Order { Id = "b-2" }, new Order { Id = "b-3" });

        Received.InOrder(() => {
            log.Record("queue:b-1");
            log.Record("queue:b-2");
            log.Record("queue:b-3");
        });
    }
}
