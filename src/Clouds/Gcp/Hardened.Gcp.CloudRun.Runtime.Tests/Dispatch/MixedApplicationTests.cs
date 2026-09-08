using DependencyModules.Testing.Attributes;
using Hardened.Gcp.CloudRun.Runtime.Dispatch;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Dispatch;

/// <summary>
/// One application, a <c>[Get]</c> route and a <c>[Queue]</c> handler, served on the pipeline
/// host through the one dispatch <c>[CloudRunRuntime]</c> composes.
/// </summary>
/// <remarks>
/// The container holds the routing table <c>[KestrelRuntime]</c> registered and the function
/// dispatch the generator registered, and what the harness and the hosts find is one
/// <see cref="IHandlerDispatch"/> that routes by scheme. <see cref="MixedApplicationOverASocketTests"/>
/// is the same two requests over Kestrel.
/// </remarks>
public class MixedApplicationTests {

    [HardenedTest]
    public async Task AWebRouteIsServedBesideAQueue(ITestWebApp app) {
        var response = await app.Get("/ping");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("pong", response.Deserialize<Pong>().Answer);
    }

    [HardenedTest]
    public async Task AQueueMessageIsServedBesideAWebRoute(MixedApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "m-1" });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "m-1"));
    }

    /// <summary>
    /// Exactly one, and it is the composite under both names: the neutral test delivery and the
    /// Lambda loop count <see cref="IHandlerDispatch"/> registrations, and Kestrel resolves
    /// <see cref="IWebExecutionHandlerService"/> without asking for the other.
    /// </summary>
    [HardenedTest]
    public void TheContainerHoldsOneDispatchUnderBothNames(IServiceProvider provider) {
        var dispatch = Assert.Single(provider.GetServices<IHandlerDispatch>());

        var composed = Assert.IsType<CloudRunDispatch>(dispatch);

        Assert.NotNull(composed.Function);
        Assert.Same(dispatch, provider.GetRequiredService<IWebExecutionHandlerService>());
    }
}

/// <summary>The same application over a Kestrel socket.</summary>
[KestrelRuntime]
public class MixedApplicationOverASocketTests {

    [HardenedTest]
    public async Task AWebRouteIsServedBesideAQueue(ITestWebApp app) {
        var response = await app.Get("/ping");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("pong", response.Deserialize<Pong>().Answer);
    }

    [HardenedTest]
    public async Task AQueueMessageIsServedBesideAWebRoute(MixedApp.Queues queues, [Mock] IOrderStore store) {
        await queues.Orders(new Order { Id = "m-socket-1" });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "m-socket-1"));
    }
}
