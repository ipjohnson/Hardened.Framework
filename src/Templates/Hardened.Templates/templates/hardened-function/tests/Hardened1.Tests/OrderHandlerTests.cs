#if (sqs)
using Hardened.Amz.Function.Sqs.Testing;
#endif
#if (invoke)
using Hardened.Amz.Function.Lambda.Testing;
#endif

namespace Hardened1.Tests;

/// <summary>
/// [HardenedTest] boots the real application and resolves the test's parameters from its
/// container, so an invocation here goes through the same pipeline a deployed function does -
/// deserialisation, the filter chain, the handler - without AWS.
/// </summary>
public class OrderHandlerTests {

#if (sqs)
    [HardenedTest]
    public async Task ARecordReachesTheHandler(TestSqsApp app, OrderLog log) {
        var response = await app.SendMessage(new Order { Id = "A-1", Quantity = 2 });

        Assert.Empty(response.BatchItemFailures);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }

    /// <summary>
    /// The batch is unpacked and the handler runs once per record, which is the whole reason to
    /// take an SQS trigger rather than read the event yourself.
    /// </summary>
    [HardenedTest]
    public async Task EveryRecordInABatchIsHandled(TestSqsApp app, OrderLog log) {
        var response = await app.SendMessage(
            new Order { Id = "A-1", Quantity = 1 },
            new Order { Id = "A-2", Quantity = 2 },
            new Order { Id = "A-3", Quantity = 3 });

        Assert.Empty(response.BatchItemFailures);
        Assert.Equal(3, log.Orders.Count);
    }
#endif
#if (invoke)
    [HardenedTest]
    public async Task ThePayloadReachesTheHandler(LambdaTestApp app, OrderLog log) {
        var accepted = await app.Invoke<OrderAccepted>(
            "Process", new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", accepted.Id);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }

    /// <summary>The return value is serialised back to the caller, not discarded.</summary>
    [HardenedTest]
    public async Task TheHandlersReturnValueComesBack(LambdaTestApp app) {
        var accepted = await app.Invoke<OrderAccepted>(
            "Process", new Order { Id = "A-2", Quantity = 1 });

        Assert.Equal(1, accepted.Received);
    }
#endif
}
