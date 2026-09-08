using DependencyModules.Testing.Attributes;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.AzureEvents.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureEvents.SUT.Tests;

/// <summary>
/// The one trigger a façade cannot express, sent through the delivery seam itself.
///
/// <para>
/// <c>[Event]</c> has no façade because it is addressed by source <em>and</em> type, two segments
/// where every other trigger has one, so a method name cannot carry it. The delivery takes the
/// route as a path, which is the same door the façades go through - so through the pipeline this
/// routes the event, and through the worker it builds the CloudEvent Event Grid would have sent
/// and reads the route back off it.
/// </para>
/// </summary>
public class EventTests {

    [HardenedTest]
    public async Task AnEventReachesItsHandlerAndBindsItsData(
        ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        await delivery.Deliver(
            [new Order { Id = "e-1", Quantity = 5 }], "EVENT", "/com.acme.orders/OrderPlaced");

        log.Received().Record("event:e-1");
    }

    /// <summary>
    /// A type this application has no handler for fails the invocation, naming the route, rather
    /// than being handed to a handler written for another: a subscription wider than the handlers
    /// is a deployment fault, and Event Grid retrying and then dead-lettering is what makes it
    /// visible.
    /// </summary>
    [HardenedTest]
    public async Task AnEventOfAnotherTypeFailsTheInvocation(
        ITriggerDelivery delivery, [Mock] ITriggerLog log) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => delivery.Deliver(
                [new Order { Id = "e-2" }], "EVENT", "/com.acme.orders/OrderCancelled"));

        Assert.Contains("EVENT /com.acme.orders/OrderCancelled", failure.Message);

        log.DidNotReceive().Record(Arg.Any<string>());
    }
}
