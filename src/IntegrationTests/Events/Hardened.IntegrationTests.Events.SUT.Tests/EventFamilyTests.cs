using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// One function serving a queue, a topic, a schedule and a bus.
///
/// <para>
/// The SQS fixture holds the single-adapter case, where nothing is ever asked about a payload.
/// This holds the other half of the family split: several sources in one function, so every
/// delivery goes through the peek. SQS and SNS both arrive as a <c>Records</c> array, which is
/// where an adapter matching the array rather than the event source value would claim both and the
/// first registered would win.
/// </para>
/// </summary>
public class EventFamilyTests {

    [HardenedTest]
    public async Task ANotificationReachesTheTopicHandler(
        ITopicsOf<EventsTestApp.Topics> topics, [Mock] ITriggerLog log) {
        await topics.PublishTo.OrderEvents(new Order { Id = "t-1" });

        log.Received().Record("topic:t-1");
    }

    /// <summary>
    /// A schedule routes on its rule rather than on aws.events/Scheduled Event, which every
    /// schedule in the account would share - and it carries no payload, so the handler takes none.
    /// </summary>
    [HardenedTest]
    public async Task AScheduledInvocationReachesTheTimerHandler(
        ITimersOf<EventsTestApp.Timers> timers, [Mock] ITriggerLog log) {
        await timers.Fire.NightlyRollup();

        log.Received().Record("timer:nightly-rollup");
    }

    [HardenedTest]
    public async Task AQueueMessageReachesTheQueueHandler(
        IQueuesOf<EventsTestApp.Queues> queues, [Mock] ITriggerLog log) {
        await queues.SendTo.OrdersNew(new Order { Id = "q-1" });

        log.Received().Record("queue:q-1");
    }

    /// <summary>
    /// The assertion the family split rests on, run through a real application: three sources,
    /// three different handlers, one function, and the peek choosing between them.
    /// </summary>
    [HardenedTest]
    public async Task EverySourceReachesItsOwnHandlerInOneFunction(
        IQueuesOf<EventsTestApp.Queues> queues,
        ITopicsOf<EventsTestApp.Topics> topics,
        ITimersOf<EventsTestApp.Timers> timers,
        [Mock] ITriggerLog log) {
        await queues.SendTo.OrdersNew(new Order { Id = "q-1" });
        await topics.PublishTo.OrderEvents(new Order { Id = "t-1" });
        await timers.Fire.NightlyRollup();

        Received.InOrder(() => {
            log.Record("queue:q-1");
            log.Record("topic:t-1");
            log.Record("timer:nightly-rollup");
        });
    }

    /// <summary>
    /// Four triggers, three adapters: EventBridge serves both the schedule and the bus event, and
    /// registering it twice would put two adapters in front of every EventBridge payload.
    /// </summary>
    [HardenedTest]
    public void FourTriggersRegisterThreeAdapters(IServiceProvider provider) {
        var adapters = provider.GetServices<IPayloadAdapter>().ToArray();

        Assert.Equal(3, adapters.Length);
        Assert.Contains(adapters, adapter => adapter is SqsAdapter);
        Assert.Contains(adapters, adapter => adapter is SnsAdapter);
        Assert.Contains(adapters, adapter => adapter is EventBridgeAdapter);
    }
}
