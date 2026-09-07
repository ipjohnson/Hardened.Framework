using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// The generated trigger façade: a typed way to say what every other fixture says in hand-written
/// JSON.
///
/// <para>
/// The queue name and the payload type are both checked by the compiler - the queue exists because
/// the method does, and the payload matches because overload resolution says so. What runs is the
/// pipeline: routing on the scheme and path, the batch fan-out, binding, every filter and the
/// handler. Nothing here names AWS, so this file is unchanged if the application moves host.
/// </para>
/// </summary>
[Collection(QueueHandlerState.Name)]
public class TriggerFacadeTests : IDisposable {
    private readonly ServiceProvider _provider;

    public TriggerFacadeTests() {
        OrderHandlers.Reset();

        _provider = new SqsTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            // Registered here rather than by the application, which is what keeps the façade out of
            // a published function: nothing app-side references it, so the linker drops it.
            (_, services) => services.AddTriggerTesting(),
            builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private SqsTestApp.Queues Queues =>
        _provider.GetRequiredService<IQueuesOf<SqsTestApp.Queues>>().SendTo;

    [Fact]
    public async Task AMessageSentThroughTheFacadeReachesTheHandler() {
        await Queues.OrdersNew(new Order { Id = "a-1", Quantity = 3 });

        var order = Assert.Single(OrderHandlers.Handled);

        Assert.Equal("a-1", order.Id);
        Assert.Equal(3, order.Quantity);
    }

    /// <summary>
    /// One message and a batch are the same call, which is why the method takes <c>params</c>. A
    /// separate single-message overload would give a test two ways to say one thing and let the two
    /// drift.
    /// </summary>
    [Fact]
    public async Task ABatchIsTheSameCall() {
        await Queues.OrdersNew(
            new Order { Id = "a-1", Quantity = 1 },
            new Order { Id = "a-2", Quantity = 2 },
            new Order { Id = "a-3", Quantity = 3 });

        Assert.Equal(["a-1", "a-2", "a-3"], OrderHandlers.Handled.Select(order => order.Id));
    }

    /// <summary>
    /// The failure policy is the pipeline's, not the façade's. A trigger rethrows, because failing
    /// the invocation is what makes a source redeliver, and a test asserting that a bad message
    /// fails has to see the exception rather than a recorded report.
    /// </summary>
    [Fact]
    public async Task TheFailurePolicyStillApplies() {
        OrderHandlers.FailFor.Add("a-2");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Queues.OrdersNew(
                new Order { Id = "a-1", Quantity = 1 },
                new Order { Id = "a-2", Quantity = 2 }));
    }
}
