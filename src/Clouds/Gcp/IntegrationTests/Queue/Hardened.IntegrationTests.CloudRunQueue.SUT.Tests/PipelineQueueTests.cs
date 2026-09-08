using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.CloudRunQueue.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT.Tests;

/// <summary>
/// The pipeline rung: the same handler reached through the neutral delivery, which builds a
/// request and runs the pipeline and names no cloud.
/// </summary>
/// <remarks>
/// What this holds is that an application on <c>[CloudRunRuntime]</c> is still a function
/// application to the harness that knows nothing about Cloud Run: one dispatch to install, a
/// batch to fan out, a handler to reach. The Kestrel host brings a routing table and the
/// generator a function table, and the runtime composes them into the one the neutral delivery
/// expects to find.
/// </remarks>
[PipelineDelivery]
public class PipelineQueueTests {

    [HardenedTest]
    public async Task AQueueMessageReachesTheHandlerThroughThePipeline(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "p-1", Quantity = 2 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "p-1" && order.Quantity == 2));
    }

    /// <summary>
    /// Through the pipeline a batch is one delivery, so a refused message fails it, the way every
    /// trigger family does when nothing reports item failures.
    /// </summary>
    [HardenedTest]
    public async Task AFailedMessageFailsTheDelivery(
        CloudRunQueueApp.Queues queues, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "p-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queues.Orders(new Order { Id = "p-1" }, new Order { Id = "p-2" }));
    }
}
