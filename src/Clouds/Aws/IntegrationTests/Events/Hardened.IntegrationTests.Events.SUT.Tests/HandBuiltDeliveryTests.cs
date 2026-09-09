using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Testing;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// The delivery built over a handler rather than over a container source.
/// </summary>
/// <remarks>
/// <para>
/// The harness never takes this route: <c>[LambdaTesting]</c> registers the delivery over the
/// source, so every send gets an environment of its own. This constructor is the other one, for a
/// harness assembled by hand that already holds a <see cref="LambdaInvocationHandler"/> and wants to
/// send envelopes to it - the same arrangement <c>PipelineHost</c>'s provider constructor offers on
/// the web side.
/// </para>
/// <para>
/// It is one handler for the life of the delivery, so it models a warm sandbox, and it is kept
/// rather than removed because a hand-built harness has no container source to hand over.
/// </para>
/// </remarks>
public class HandBuiltDeliveryTests {

    [HardenedTest]
    public async Task ADeliveryOverAHandlerReachesTheHandler(
        IServiceProvider provider, [Mock] ITriggerLog log) {
        var delivery = new LambdaEnvelopeDelivery(
            provider.GetRequiredService<LambdaInvocationHandler>());

        await delivery.Deliver([new Order { Id = "h-1" }], "QUEUE", "/orders-new");

        log.Received().Record("queue:h-1");
    }

    /// <summary>
    /// One handler for every send, which is what makes this the warm arrangement.
    /// </summary>
    [HardenedTest]
    public async Task EverySendReachesTheSameHandler(
        IServiceProvider provider, [Mock] ITriggerLog log) {
        var delivery = new LambdaEnvelopeDelivery(
            provider.GetRequiredService<LambdaInvocationHandler>());

        await delivery.Deliver([new Order { Id = "h-1" }], "QUEUE", "/orders-new");
        await delivery.Deliver([new Order { Id = "h-2" }], "QUEUE", "/orders-new");

        Received.InOrder(() => {
            log.Record("queue:h-1");
            log.Record("queue:h-2");
        });
    }
}
