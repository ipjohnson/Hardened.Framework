using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.SUT.Tests;

/// <summary>
/// A queue function, whole: the trigger attribute, the generators, the module the build property
/// named, the adapter, the batch filter and the invocation handler.
///
/// <para>
/// The SQS fixture's test file with the queue's name changed, on purpose: the claim under test is
/// that a handler written for one provider moves to another by changing a package reference, and
/// a test file that had to change with it would be evidence against. It is compiled into two test
/// projects - this one, which delivers through the pipeline, and the worker rung, which delivers
/// the trigger data the isolated worker would bind - and passes in both.
/// </para>
/// </summary>
public class QueueTests {

    /// <summary>
    /// The claim the whole design rests on: a handler that names a queue and nothing else is
    /// reached by a message from that queue.
    /// </summary>
    [HardenedTest]
    public async Task AQueueMessageReachesTheHandler(
        AzureQueueTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "a-1", Quantity = 2 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 2));
    }

    /// <summary>
    /// One invocation, one handler call per message. The route was chosen once from the queue the
    /// batch arrived against; the fan-out is the filter.
    /// </summary>
    [HardenedTest]
    public async Task EveryMessageInABatchIsHandledSeparately(
        AzureQueueTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" });

        store.Received(3).Place(Arg.Any<Order>());
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    /// <summary>
    /// Each fork binds its own message's body, so a handler sees what was published rather than
    /// the batch it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachMessageBindsItsOwnBody(
        AzureQueueTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "a-1", Quantity = 10 }, new Order { Id = "a-2", Quantity = 20 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 10));
        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-2" && order.Quantity == 20));
    }

    /// <summary>
    /// Failing the invocation is what abandons the batch so the queue redelivers. With per-message
    /// settlement off - the default until Phase 2 - a failed message has to take the whole batch
    /// with it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedMessageFailsTheInvocation(
        AzureQueueTestApp.Queues queues, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queues.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" }));
    }
}
