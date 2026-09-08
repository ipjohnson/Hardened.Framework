using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.CloudRunQueue.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT.Tests;

/// <summary>
/// A queue service, whole: the trigger attribute, the generator, the module the build property
/// named, the push envelope, the front door, the one dispatch and the batch filter.
///
/// <para>
/// Every test declares what it needs and the runner supplies it - the façade to send through, a
/// substitute for the collaborator the handler resolves. The same four tests the SQS fixture
/// holds, because the claim is that a handler moves between the two by changing a package
/// reference; here they run on the pipeline host, and <see cref="QueueOverASocketTests"/> runs
/// them over a Kestrel socket.
/// </para>
/// </summary>
public class QueueTests {

    /// <summary>
    /// The claim the whole design rests on: a handler that names a queue and nothing else is
    /// reached by a message from that queue.
    /// </summary>
    [HardenedTest]
    public async Task AQueueMessageReachesTheHandler(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "a-1", Quantity = 2 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 2));
    }

    /// <summary>
    /// One push per message, one handler call per push. Pub/Sub never batches, so a batch a test
    /// sends is several deliveries, each acknowledged on its own.
    /// </summary>
    [HardenedTest]
    public async Task EveryMessageIsHandledSeparately(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" });

        store.Received(3).Place(Arg.Any<Order>());
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    /// <summary>
    /// Each push decodes its own data, so a handler sees what was published rather than the
    /// envelope it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachMessageBindsItsOwnBody(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "a-1", Quantity = 10 }, new Order { Id = "a-2", Quantity = 20 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 10));
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2" && order.Quantity == 20));
    }

    /// <summary>
    /// A handler that throws is answered outside the acknowledging statuses, which is what makes
    /// Pub/Sub redeliver. The message after the refused one is still delivered and handled,
    /// because each push is acknowledged on its own.
    /// </summary>
    [HardenedTest]
    public async Task AFailedMessageIsNotAcknowledged(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queues.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" }));

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-3"));
    }
}
