using Hardened.Gcp.CloudRun.Runtime.Dispatch;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Gcp.Functions.Runtime.Tests.Hosting;

/// <summary>
/// One application served through the Functions Framework's entry point: a web route, a queue
/// message, and what happens when the chain throws.
/// </summary>
public class CloudFunctionHostTests
{
    [Fact]
    public async Task AWebRouteIsServed()
    {
        using var fixture = new CloudFunctionFixture();

        var context = await fixture.Deliver(services =>
            Deliveries.Request("GET", "/ping", services)
        );

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("""{"answer":"pong"}""", Deliveries.Text(context));
    }

    /// <summary>
    /// The whole fan-out in one delivery: the front door recognises the push, forks the chain with
    /// a <c>QUEUE /orders</c> request, and the dispatch sends that through the function table.
    /// None of it is this package's code, which is the point - the bridge built the context and
    /// the Cloud Run runtime did the rest.
    /// </summary>
    [Fact]
    public async Task APubSubPushReachesTheQueueHandler()
    {
        var store = new RecordingOrderStore();

        using var fixture = new CloudFunctionFixture(services =>
            services.AddSingleton<IOrderStore>(store)
        );

        var context = await fixture.Deliver(services =>
            Deliveries.Push(services, "orders", """{"id":"m-1"}""")
        );

        var order = Assert.Single(store.Placed);

        Assert.Equal("m-1", order.Id);
        Assert.Equal(200, context.Response.StatusCode);
    }

    /// <summary>
    /// A push for a subscription no handler declares is not acknowledged.
    /// </summary>
    /// <remarks>
    /// 500, because <c>FunctionDispatchFilter</c> raises rather than answering and
    /// <see cref="HostFailurePolicy.Answer500"/> turns that into a status - the same 500 the Cloud
    /// Run host answers, and one of the codes Pub/Sub reads as a negative acknowledgement so the
    /// message is redelivered. What it must not be is a 404 from the web table, which would mean
    /// the front door did not recognise the push at all.
    /// </remarks>
    [Fact]
    public async Task AnUnroutedPushIsNotAcknowledged()
    {
        using var fixture = new CloudFunctionFixture(services =>
            services.AddSingleton<IOrderStore>(new RecordingOrderStore())
        );

        var context = await fixture.Deliver(services =>
            Deliveries.Push(services, "nothing-declares-this", """{"id":"m-2"}""")
        );

        Assert.Equal(500, context.Response.StatusCode);
    }

    /// <summary>
    /// <see cref="HostFailurePolicy.Answer500"/>, so the application's own logger sees the failure
    /// and Google's pipeline is handed a finished response rather than an exception.
    /// </summary>
    [Fact]
    public async Task AThrowingHandlerIsAnsweredRatherThanPropagated()
    {
        using var fixture = new CloudFunctionFixture(services =>
            services.AddSingleton<IOrderStore>(new ThrowingOrderStore())
        );

        var context = await fixture.Deliver(services =>
            Deliveries.Push(services, "orders", """{"id":"m-3"}""")
        );

        Assert.Equal(500, context.Response.StatusCode);
    }

    /// <summary>
    /// The request scope belongs to the host. The bridge creates none and disposes none, which is
    /// what lets a handler resolve a scoped service the host also resolves.
    /// </summary>
    [Fact]
    public async Task TheRequestScopeIsTheHostsAndSurvivesTheDelivery()
    {
        using var fixture = new CloudFunctionFixture();

        using var scope = fixture.Provider.CreateScope();

        var context = Deliveries.Request("GET", "/ping", scope.ServiceProvider);

        await fixture.Host.HandleAsync(context);

        // Resolving after the delivery is what a disposed scope refuses.
        Assert.NotNull(scope.ServiceProvider.GetService<IKnownServices>());
    }

    /// <summary>
    /// Exactly one, and it is the composite: the bridge registers no dispatch of its own, and
    /// <c>[CloudRunRuntime]</c>'s decorator composed the routing table and the function table the
    /// same way it does under Kestrel.
    /// </summary>
    [Fact]
    public void TheContainerHoldsTheOneCloudRunDispatch()
    {
        using var fixture = new CloudFunctionFixture();

        var dispatch = Assert.Single(fixture.Provider.GetServices<IHandlerDispatch>());

        var composed = Assert.IsType<CloudRunDispatch>(dispatch);

        Assert.NotNull(composed.Function);
        Assert.Same(dispatch, fixture.Provider.GetRequiredService<IWebExecutionHandlerService>());
    }
}

internal sealed class ThrowingOrderStore : IOrderStore
{
    public void Place(Order order) => throw new InvalidOperationException("no");
}
