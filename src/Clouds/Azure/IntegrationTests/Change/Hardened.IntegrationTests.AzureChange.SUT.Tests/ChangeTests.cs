using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.AzureChange.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureChange.SUT.Tests;

/// <summary>
/// A change feed function, whole: the trigger attribute, the generators, the module the build
/// property named, the adapter, the split, the batch filter and the invocation handler.
///
/// <para>
/// The DynamoDB fixture's test file, less the test that reaches the row's raw image: a change
/// feed document is plain JSON and carries no image, old or new, so there is nothing for that
/// test to reach. It is compiled into two test projects, the pipeline rung and the worker rung -
/// where the delivery writes the documents into one feed array with the system properties Cosmos
/// stamps on them, so what the adapter splits is what the host sends - and passes in both.
/// </para>
/// </summary>
public class ChangeTests {

    /// <summary>
    /// The claim the adapter rests on: a handler that names a container and binds a plain type is
    /// reached with the document, and never sees the feed.
    /// </summary>
    [HardenedTest]
    public async Task AChangeReachesTheHandlerAsThePlainDocument(
        AzureChangeTestApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "a-1", Quantity = 2, Total = 42.5m });

        projection.Received().Apply(Arg.Is<Order>(
            order => order.Id == "a-1" && order.Quantity == 2 && order.Total == 42.5m));
    }

    /// <summary>
    /// One invocation, one handler call per change. The route was chosen once from the container
    /// the batch arrived against; the fan-out is the filter.
    /// </summary>
    [HardenedTest]
    public async Task EveryChangeInABatchIsHandledSeparately(
        AzureChangeTestApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(
            new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" });

        projection.Received(3).Apply(Arg.Any<Order>());
        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    /// <summary>
    /// Each fork binds its own document, so a handler sees the document that changed rather than
    /// the batch it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachChangeBindsItsOwnDocument(
        AzureChangeTestApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(
            new Order { Id = "a-1", Quantity = 10 }, new Order { Id = "a-2", Quantity = 20 });

        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-1" && o.Quantity == 10));
        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-2" && o.Quantity == 20));
    }

    /// <summary>
    /// One container is one route: a change on the audit container reaches its own handler and
    /// not the orders one.
    /// </summary>
    [HardenedTest]
    public async Task AChangeOnAnotherContainerReachesItsOwnHandler(
        AzureChangeTestApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Audit(new Order { Id = "a-9", Total = 7 });

        projection.Received().Audit(Arg.Is<Order>(order => order.Id == "a-9" && order.Total == 7));
        projection.DidNotReceive().Apply(Arg.Any<Order>());
    }

    /// <summary>
    /// Failing the invocation is what keeps the lease where it was, so the feed replays. Nothing
    /// reports individual changes, so a failed change has to take the whole batch with it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedChangeFailsTheInvocation(
        AzureChangeTestApp.Changes changes, [Mock] IOrderProjection projection) {
        projection.When(one => one.Apply(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => changes.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" }));
    }
}
