using DependencyModules.Testing.Attributes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunChange.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunChange.SUT.Tests;

/// <summary>
/// A change feed service, whole: the trigger attribute, the generator, the module the build
/// property named, the Firestore envelope, its value conversion, the front door and the one
/// dispatch. The same tests the DynamoDB fixture holds, on the pipeline host.
/// </summary>
public class ChangeTests {

    /// <summary>A handler that names a collection and binds a plain type is reached with the document, and never sees a typed value.</summary>
    [HardenedTest]
    public async Task AChangeReachesTheHandlerAsThePlainDocument(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "a-1", Quantity = 2, Total = 42.5m });

        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 2 && order.Total == 42.5m));
    }

    [HardenedTest]
    public async Task EveryChangeIsHandledSeparately(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" }, new Order { Id = "a-3" });

        projection.Received(3).Apply(Arg.Any<Order>());
        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    [HardenedTest]
    public async Task EachChangeBindsItsOwnDocument(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "a-1", Quantity = 10 }, new Order { Id = "a-2", Quantity = 20 });

        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-1" && o.Quantity == 10));
        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-2" && o.Quantity == 20));
    }

    /// <summary>
    /// <c>[OldValue]</c> reaches the document as it was, as the handler's own type. The delivery
    /// sends the same document as both values, so the previous one is what was sent.
    /// </summary>
    [HardenedTest]
    public async Task ThePreviousDocumentIsReachableThroughOldValue(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Audit(new Order { Id = "a-9", Total = 7 });

        projection.Received().Previous(Arg.Is<Order?>(previous => previous != null && previous.Id == "a-9" && previous.Total == 7));
    }

    [HardenedTest]
    public async Task AFailedChangeIsNotAcknowledged(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        projection.When(one => one.Apply(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => changes.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" }));
    }
}

/// <summary>The same changes over a Kestrel socket, protobuf body and all.</summary>
[KestrelRuntime]
public class ChangeOverASocketTests {

    [HardenedTest]
    public async Task AChangeReachesTheHandlerAsThePlainDocument(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "s-1", Quantity = 2, Total = 42.5m });

        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "s-1" && order.Quantity == 2 && order.Total == 42.5m));
    }

    [HardenedTest]
    public async Task ThePreviousDocumentIsReachableThroughOldValue(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Audit(new Order { Id = "s-9", Total = 7 });

        projection.Received().Previous(Arg.Is<Order?>(previous => previous != null && previous.Id == "s-9"));
    }
}

/// <summary>The plain-document handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineChangeTests {

    [HardenedTest]
    public async Task AChangeReachesTheHandlerThroughThePipeline(CloudRunChangeApp.Changes changes, [Mock] IOrderProjection projection) {
        await changes.Orders(new Order { Id = "p-1", Quantity = 2 });

        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "p-1" && order.Quantity == 2));
    }
}
