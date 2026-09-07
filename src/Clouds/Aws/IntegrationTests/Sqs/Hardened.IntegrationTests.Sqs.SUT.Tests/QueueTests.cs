using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Testing.Attributes;
using DependencyModules.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// A queue function, whole: the trigger attribute, the generator, the module the build property
/// named, the adapter, the batch filter and the invocation loop.
///
/// <para>
/// Every test declares what it needs and the runner supplies it - the façade to send through, a
/// substitute for the collaborator the handler resolves. There is no fixture constructor, no
/// service provider to build and nothing to dispose, which is what the web suite has always looked
/// like and what these used to lack.
/// </para>
/// </summary>
public class QueueTests {

    /// <summary>
    /// The claim the whole design rests on: a handler that names a queue and nothing else is
    /// reached by a message from that queue.
    /// </summary>
    [HardenedTest]
    public async Task AQueueMessageReachesTheHandler(
        SqsTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.OrdersNew(new Order { Id = "a-1", Quantity = 2 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 2));
    }

    /// <summary>
    /// One invocation, one handler call per message. The route was chosen once from the queue the
    /// batch arrived against; the fan-out is the filter.
    /// </summary>
    [HardenedTest]
    public async Task EveryMessageInABatchIsHandledSeparately(
        SqsTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.OrdersNew(
            new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" });

        store.Received(3).Place(Arg.Any<Order>());
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    /// <summary>
    /// Each fork binds its own record's body, so a handler sees what was published rather than the
    /// batch it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachMessageBindsItsOwnBody(
        SqsTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.OrdersNew(
            new Order { Id = "a-1", Quantity = 10 }, new Order { Id = "a-2", Quantity = 20 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 10));
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2" && order.Quantity == 20));
    }

    /// <summary>
    /// Failing the invocation is what returns a message to the queue. With ReportBatchItemFailures
    /// off - the default, because a report sent to a mapping that did not ask for one is discarded -
    /// a failed message has to take the whole batch with it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedMessageFailsTheInvocation(
        SqsTestApp.Queues queues, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queues.OrdersNew(new Order { Id = "a-1" }, new Order { Id = "a-2" }));
    }
}
