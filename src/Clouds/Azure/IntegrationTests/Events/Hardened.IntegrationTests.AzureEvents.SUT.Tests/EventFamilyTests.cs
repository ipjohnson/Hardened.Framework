using DependencyModules.Testing.Attributes;
using Hardened.Azure.Functions.EventGrid;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Timer;
using Hardened.IntegrationTests.AzureEvents.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureEvents.SUT.Tests;

/// <summary>
/// One function app serving a queue, a topic, a schedule and an event source.
///
/// <para>
/// The queue fixture holds the single-adapter case. This holds the other half: several sources
/// in one application, each with its own generated function, so every delivery has to reach the
/// adapter its shim was generated for - and Service Bus's has to tell a queue from a topic by the
/// route alone, because both arrive as the same message type.
/// </para>
/// </summary>
public class EventFamilyTests {

    [HardenedTest]
    public async Task ANotificationReachesTheTopicHandler(
        AzureEventsTestApp.Topics topics, [Mock] ITriggerLog log) {
        await topics.OrderEvents(new Order { Id = "t-1" });

        log.Received().Record("topic:t-1");
    }

    /// <summary>
    /// A schedule routes on its name, and carries no payload, so the handler takes none.
    /// </summary>
    [HardenedTest]
    public async Task AScheduledInvocationReachesTheTimerHandler(
        AzureEventsTestApp.Timers timers, [Mock] ITriggerLog log) {
        await timers.NightlyRollup();

        log.Received().Record("timer:nightly-rollup");
    }

    [HardenedTest]
    public async Task AQueueMessageReachesTheQueueHandler(
        AzureEventsTestApp.Queues queues, [Mock] ITriggerLog log) {
        await queues.OrdersNew(new Order { Id = "q-1" });

        log.Received().Record("queue:q-1");
    }

    /// <summary>
    /// Three sources, three different handlers, one application, each delivery reaching its own.
    /// </summary>
    [HardenedTest]
    public async Task EverySourceReachesItsOwnHandlerInOneFunctionApp(
        AzureEventsTestApp.Queues queues,
        AzureEventsTestApp.Topics topics,
        AzureEventsTestApp.Timers timers,
        [Mock] ITriggerLog log) {
        await queues.OrdersNew(new Order { Id = "q-1" });
        await topics.OrderEvents(new Order { Id = "t-1" });
        await timers.NightlyRollup();

        Received.InOrder(() => {
            log.Record("queue:q-1");
            log.Record("topic:t-1");
            log.Record("timer:nightly-rollup");
        });
    }

    /// <summary>
    /// Four triggers, three adapters: Service Bus serves both the queue and the topic, and
    /// registering it twice would put two adapters in front of every Service Bus batch.
    /// </summary>
    [HardenedTest]
    public void FourTriggersRegisterThreeAdapters(IServiceProvider provider) {
        var adapters = provider.GetServices<ITriggerAdapter>().ToArray();

        Assert.Equal(3, adapters.Length);
        Assert.Contains(adapters, adapter => adapter is ServiceBusAdapter);
        Assert.Contains(adapters, adapter => adapter is TimerAdapter);
        Assert.Contains(adapters, adapter => adapter is EventGridAdapter);
    }
}
