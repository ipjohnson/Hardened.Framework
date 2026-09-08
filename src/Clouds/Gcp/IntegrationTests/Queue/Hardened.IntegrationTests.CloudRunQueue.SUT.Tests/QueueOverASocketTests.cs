using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.CloudRunQueue.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT.Tests;

/// <summary>
/// <see cref="QueueTests"/> over a Kestrel socket: the push is a real HTTP request to a port the
/// kernel picked, Kestrel builds the request, the front door unwraps it and the status that comes
/// back is what Pub/Sub would read.
/// </summary>
/// <remarks>
/// The bodies are the same as the pipeline class's on purpose. What differs is the attribute on
/// this class and what the wire changes: a handler's exception does not cross it, so a refused
/// message is seen here as the negative acknowledgement the service answered rather than as the
/// exception itself.
/// </remarks>
[KestrelRuntime]
public class QueueOverASocketTests {

    [HardenedTest]
    public async Task AQueueMessageReachesTheHandler(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "s-1", Quantity = 2 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-1" && order.Quantity == 2));
    }

    [HardenedTest]
    public async Task EveryMessageIsHandledSeparately(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "s-1" }, new Order { Id = "s-2" }, new Order { Id = "s-3" });

        store.Received(3).Place(Arg.Any<Order>());
        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-2"));
    }

    [HardenedTest]
    public async Task EachMessageBindsItsOwnBody(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(
            new Order { Id = "s-1", Quantity = 10 }, new Order { Id = "s-2", Quantity = 20 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-1" && order.Quantity == 10));
        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-2" && order.Quantity == 20));
    }

    [HardenedTest]
    public async Task AFailedMessageIsNotAcknowledged(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "s-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queues.Orders(new Order { Id = "s-1" }, new Order { Id = "s-2" }, new Order { Id = "s-3" }));

        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-3"));
    }
}
