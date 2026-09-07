using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.Aws.Lambda.Invoke;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.Invoke.SUT.Tests;

/// <summary>
/// A directly invoked function: whatever the caller sent, and an answer back.
///
/// <para>
/// The one façade whose methods return something. Every trigger is fire-and-forget - a queue
/// message is handled or redelivered - but a direct invocation has a caller waiting, so
/// <c>Call.Handle(...)</c> answers with the handler's own type rather than a task with nothing in
/// it.
/// </para>
/// </summary>
public class DirectInvokeTests {

    [HardenedTest]
    public async Task ACallersPayloadReachesTheHandler(
        InvokeTestApp.Invocations invocations, [Mock] IOrderLog log) {
        await invocations.Handle(new OrderRequest { Id = "o-1", Quantity = 3 });

        log.Received().Placed(
            Arg.Is<OrderRequest>(request => request.Id == "o-1" && request.Quantity == 3));
    }

    /// <summary>
    /// The answer is the payload, and the façade hands it back typed - which is the whole reason
    /// invoke gets a façade shape of its own.
    /// </summary>
    [HardenedTest]
    public async Task TheHandlersReturnValueIsTheResponse(
        InvokeTestApp.Invocations invocations, [Mock] IOrderLog log) {
        var receipt = await invocations.Handle(new OrderRequest { Id = "o-1", Quantity = 3 });

        Assert.Equal("o-1", receipt.Id);
        Assert.Equal("placed", receipt.Status);
    }

    /// <summary>
    /// The reason this family gets a function of its own. Every one of these would be claimed by an
    /// event adapter if one were registered here, and every one is legitimately the caller's.
    /// </summary>
    [HardenedTest]
    [InlineData("records")]
    [InlineData("requestContext")]
    public async Task APayloadShapedLikeAnAwsEventIsStillTheCallers(
        string field, InvokeTestApp.Invocations invocations, [Mock] IOrderLog log) {
        var request = new OrderRequest { Id = "o-1" };

        if (field == "records") {
            request.Records = ["a", "b"];
        }
        else {
            request.RequestContext = "from-billing";
        }

        var receipt = await invocations.Handle(request);

        Assert.Equal("o-1", receipt.Id);

        log.Received().Placed(Arg.Is<OrderRequest>(one => one.Id == "o-1"));
    }

    /// <summary>
    /// One adapter, so nothing is ever asked about a payload. Required rather than an optimisation
    /// here: a caller may send anything, and asking would mean parsing something that need not be
    /// JSON.
    /// </summary>
    [HardenedTest]
    public void TheInvokeAdapterIsTheOnlyOne(IServiceProvider provider) {
        Assert.IsType<InvokeAdapter>(Assert.Single(provider.GetServices<IPayloadAdapter>()));
    }
}
