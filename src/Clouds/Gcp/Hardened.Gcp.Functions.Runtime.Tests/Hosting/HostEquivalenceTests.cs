using DependencyModules.Testing.Attributes;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Gcp.Functions.Runtime.Tests.Hosting;

/// <summary>
/// The same application, the same two deliveries, on both Google hosts.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the bridge exists to pass. Everything below the entry point is one compiled
/// path - <c>FeatureExecutionRequest</c>, <c>FeatureExecutionResponse</c>, the front door, the
/// dispatch, the routing table and the handler - so a delivery through <c>HttpContext.Features</c>
/// and a delivery through the pipeline host have to reach the same handler and produce the same
/// bytes. Where they do, a measured difference between the two deployments is the deployment and
/// not the framework, which is the whole claim the benchmark rests on.
/// </para>
/// <para>
/// The two arms build separate containers, because they are separate processes in the deployment
/// being compared. That is why the queue arm asserts on its own store rather than a shared one.
/// </para>
/// </remarks>
public class HostEquivalenceTests
{
    [HardenedTest]
    public async Task AWebRouteAnswersTheSameOnBothHosts(ITestWebApp app)
    {
        var pipeline = await app.Get("/ping");

        using var fixture = new CloudFunctionFixture();

        var functions = await fixture.Deliver(services =>
            Deliveries.Request("GET", "/ping", services)
        );

        Assert.Equal(pipeline.StatusCode, functions.Response.StatusCode);
        Assert.Equal(await pipeline.ReadTextAsync(), Deliveries.Text(functions));
    }

    /// <summary>
    /// The pipeline arm is handed the message directly, because that is what the neutral test
    /// delivery does; the Cloud Functions arm is handed the push body Pub/Sub would have sent, so
    /// the envelope, the front door and the dispatch all run before the same handler is reached
    /// with the same order.
    /// </summary>
    [HardenedTest]
    public async Task AQueueMessageReachesTheSameHandlerOnBothHosts(
        FunctionsApp.Queues queues,
        [Mock] IOrderStore pipelineStore
    )
    {
        await queues.Orders(new Order { Id = "m-1" });

        pipelineStore.Received().Place(Arg.Is<Order>(order => order.Id == "m-1"));

        var functionsStore = new RecordingOrderStore();

        using var fixture = new CloudFunctionFixture(services =>
            services.AddSingleton<IOrderStore>(functionsStore)
        );

        await fixture.Deliver(services => Deliveries.Push(services, "orders", """{"id":"m-1"}"""));

        var placed = Assert.Single(functionsStore.Placed);

        Assert.Equal("m-1", placed.Id);
    }
}
