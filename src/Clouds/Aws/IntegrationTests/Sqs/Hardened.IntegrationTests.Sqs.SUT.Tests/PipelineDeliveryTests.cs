using DependencyModules.Testing.Attributes;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// One class in an assembly that declared <c>[LambdaTesting]</c>, opted back to the neutral
/// delivery, so the same test file can hold a fixture's tests at two rungs.
/// </summary>
/// <remarks>
/// The assembly attribute replaced <see cref="ITriggerDelivery"/> with the Lambda envelope
/// delivery; this class puts the pipeline one back. Which one a test is running under is a
/// resolvable fact rather than a guess, and that is what the first test asserts.
/// </remarks>
[PipelineDelivery]
public class PipelineDeliveryTests {

    [HardenedTest]
    public void TheClassAttributeWinsOverTheAssemblyAttribute(IServiceProvider provider) {
        Assert.IsType<PipelineDelivery>(provider.GetRequiredService<ITriggerDelivery>());
    }

    [HardenedTest]
    public async Task AQueueMessageReachesTheHandlerThroughThePipelineAlone(
        SqsTestApp.Queues queues, [Mock] IOrderStore store) {
        await queues.OrdersNew(new Order { Id = "p-1", Quantity = 5 });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "p-1" && order.Quantity == 5));
    }
}
