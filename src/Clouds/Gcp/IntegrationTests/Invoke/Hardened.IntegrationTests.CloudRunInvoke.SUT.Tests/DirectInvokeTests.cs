using DependencyModules.Testing.Attributes;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.CloudRunInvoke.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunInvoke.SUT.Tests;

/// <summary>
/// A directly invoked service: whatever the caller sent, and an answer back. The same tests the
/// AWS invoke fixture holds, on the pipeline host; the one façade whose methods return something.
/// </summary>
public class DirectInvokeTests {

    [HardenedTest]
    public async Task ACallersPayloadReachesTheHandler(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        await invocations.Handle(new OrderRequest { Id = "o-1", Quantity = 3 });

        log.Received().Placed(Arg.Is<OrderRequest>(request => request.Id == "o-1" && request.Quantity == 3));
    }

    /// <summary>The answer is the response body, and the façade hands it back typed.</summary>
    [HardenedTest]
    public async Task TheHandlersReturnValueIsTheResponse(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        var receipt = await invocations.Handle(new OrderRequest { Id = "o-1", Quantity = 3 });

        Assert.Equal("o-1", receipt.Id);
        Assert.Equal("placed", receipt.Status);
    }

    /// <summary>Nothing inspects a caller's payload on Cloud Run: fields named like an event's are the caller's.</summary>
    [HardenedTest]
    public async Task APayloadShapedLikeAnEventIsStillTheCallers(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        var receipt = await invocations.Handle(new OrderRequest { Id = "o-1", Records = ["a", "b"], RequestContext = "from-billing" });

        Assert.Equal("o-1", receipt.Id);

        log.Received().Placed(Arg.Is<OrderRequest>(one => one.Id == "o-1" && one.Records.Count == 2));
    }

    /// <summary>A handler that throws is a failure the caller reads as the status, not a receipt.</summary>
    [HardenedTest]
    public async Task AFailedInvocationIsAnError(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        log.When(one => one.Placed(Arg.Any<OrderRequest>())).Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => invocations.Handle(new OrderRequest { Id = "o-2" }));
    }
}

/// <summary>The same invocations over a Kestrel socket.</summary>
[KestrelRuntime]
public class DirectInvokeOverASocketTests {

    [HardenedTest]
    public async Task TheHandlersReturnValueIsTheResponse(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        var receipt = await invocations.Handle(new OrderRequest { Id = "s-1", Quantity = 3 });

        Assert.Equal("s-1", receipt.Id);
        Assert.Equal("placed", receipt.Status);

        log.Received().Placed(Arg.Is<OrderRequest>(request => request.Id == "s-1"));
    }

    [HardenedTest]
    public async Task AFailedInvocationIsAnError(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        log.When(one => one.Placed(Arg.Any<OrderRequest>())).Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => invocations.Handle(new OrderRequest { Id = "s-2" }));
    }
}

/// <summary>The same handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineInvokeTests {

    [HardenedTest]
    public async Task TheHandlersReturnValueIsTheResponseThroughThePipeline(CloudRunInvokeApp.Invocations invocations, [Mock] IOrderLog log) {
        var receipt = await invocations.Handle(new OrderRequest { Id = "p-1", Quantity = 3 });

        Assert.Equal("p-1", receipt.Id);
        Assert.Equal("placed", receipt.Status);
    }
}
