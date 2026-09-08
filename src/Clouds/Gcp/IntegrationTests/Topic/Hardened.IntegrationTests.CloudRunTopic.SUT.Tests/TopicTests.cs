using DependencyModules.Testing.Attributes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunTopic.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunTopic.SUT.Tests;

/// <summary>
/// A topic service, whole: the trigger attribute, the generator, the module the build property
/// named, the Eventarc envelope, the front door and the one dispatch. The same tests the SNS half
/// of the AWS events fixture holds, on the pipeline host.
/// </summary>
public class TopicTests {

    [HardenedTest]
    public async Task ANotificationReachesTheTopicHandler(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        await topics.OrderEvents(new Order { Id = "t-1", Quantity = 3 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "t-1" && order.Quantity == 3));
    }

    [HardenedTest]
    public async Task EveryMessageIsHandledSeparately(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        await topics.OrderEvents(new Order { Id = "t-1" }, new Order { Id = "t-2" }, new Order { Id = "t-3" });

        store.Received(3).Place(Arg.Any<Order>());
        store.Received().Place(Arg.Is<Order>(order => order.Id == "t-2"));
    }

    /// <summary>Eventarc reads anything outside 2xx as a failed delivery and retries it; the message after a refused one still arrives.</summary>
    [HardenedTest]
    public async Task AFailedMessageIsNotAcknowledged(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "t-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => topics.OrderEvents(new Order { Id = "t-1" }, new Order { Id = "t-2" }, new Order { Id = "t-3" }));

        store.Received().Place(Arg.Is<Order>(order => order.Id == "t-3"));
    }
}

/// <summary>The same tests over a Kestrel socket.</summary>
[KestrelRuntime]
public class TopicOverASocketTests {

    [HardenedTest]
    public async Task ANotificationReachesTheTopicHandler(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        await topics.OrderEvents(new Order { Id = "s-1", Quantity = 3 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-1" && order.Quantity == 3));
    }

    [HardenedTest]
    public async Task AFailedMessageIsNotAcknowledged(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Is<Order>(order => order.Id == "s-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => topics.OrderEvents(new Order { Id = "s-1" }, new Order { Id = "s-2" }, new Order { Id = "s-3" }));

        store.Received().Place(Arg.Is<Order>(order => order.Id == "s-3"));
    }
}

/// <summary>The same handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineTopicTests {

    [HardenedTest]
    public async Task ANotificationReachesTheTopicHandlerThroughThePipeline(CloudRunTopicApp.Topics topics, [Mock] IOrderStore store) {
        await topics.OrderEvents(new Order { Id = "p-1", Quantity = 3 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "p-1" && order.Quantity == 3));
    }
}
