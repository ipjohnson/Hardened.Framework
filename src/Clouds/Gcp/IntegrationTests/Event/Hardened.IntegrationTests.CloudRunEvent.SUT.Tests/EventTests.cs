using DependencyModules.Testing.Attributes;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.CloudRunEvent.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunEvent.SUT.Tests;

/// <summary>
/// An event service, whole: the trigger attribute, the generator, the module the build property
/// named, the Eventarc envelope over Hardened.CloudEvents, the front door and the one dispatch.
///
/// <para>
/// <c>[Event]</c> has no façade, because it is addressed by source and type - two segments where
/// every other trigger has one - so these send through the delivery itself, which is what a façade
/// would have called. The route is the same string the attribute declared.
/// </para>
/// </summary>
public class EventTests {
    private const string Route = "/com.acme.orders/OrderPlaced";

    [HardenedTest]
    public async Task AnEventReachesItsHandlerAndBindsItsData(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await delivery.Deliver([new Order { Id = "e-1", Quantity = 5 }], "EVENT", Route);

        log.Received().Record("event:e-1");
    }

    /// <summary>The wire shape itself, posted by hand: binary mode, the attributes in headers, the data as the body.</summary>
    [HardenedTest]
    public async Task ABinaryCloudEventPostedToTheServiceReachesTheHandler(ITestWebApp app, [Mock] ITriggerLog log) {
        var response = await app.Post(new Order { Id = "e-2" }, "/", request => {
            request.Headers["ce-specversion"] = "1.0";
            request.Headers["ce-id"] = "1234";
            request.Headers["ce-source"] = "com.acme.orders";
            request.Headers["ce-type"] = "OrderPlaced";
        });

        Assert.Equal(200, response.StatusCode);

        log.Received().Record("event:e-2");
    }

    /// <summary>A source the deployment wired that no handler asked for is a failure Eventarc sees, not a message swallowed.</summary>
    [HardenedTest]
    public async Task AnEventNoHandlerDeclaredIsAFailure(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => delivery.Deliver([new Order { Id = "e-3" }], "EVENT", "/com.acme.orders/OrderCancelled"));

        log.DidNotReceive().Record(Arg.Any<string>());
    }
}

/// <summary>The same events over a Kestrel socket.</summary>
[KestrelRuntime]
public class EventOverASocketTests {

    [HardenedTest]
    public async Task AnEventReachesItsHandlerAndBindsItsData(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await delivery.Deliver([new Order { Id = "s-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

        log.Received().Record("event:s-1");
    }

    [HardenedTest]
    public async Task AnEventNoHandlerDeclaredIsAFailure(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => delivery.Deliver([new Order { Id = "s-3" }], "EVENT", "/com.acme.orders/OrderCancelled"));
    }
}

/// <summary>The same handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineEventTests {

    [HardenedTest]
    public async Task AnEventReachesItsHandlerThroughThePipeline(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await delivery.Deliver([new Order { Id = "p-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

        log.Received().Record("event:p-1");
    }
}
