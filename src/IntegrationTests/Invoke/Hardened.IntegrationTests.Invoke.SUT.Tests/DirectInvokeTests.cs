using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Invoke.SUT.Tests;

/// <summary>
/// A directly invoked function: whatever the caller sent, and an answer back.
///
/// <para>
/// The family that must not share a function with the others, and this is where that stops being an
/// argument. The payloads carry fields called <c>records</c> and <c>requestContext</c> - the fields
/// SQS, SNS, streams and API Gateway are recognised by - because a caller is entitled to them and
/// nothing can tell them from AWS's by inspection.
/// </para>
/// <para>
/// Sent as raw payloads rather than through a façade, because there is no source to send from: a
/// direct invocation is the caller's own bytes, which is the whole distinction.
/// </para>
/// </summary>
public class DirectInvokeTests {

    private static async Task<string> Invoke(
        IServiceProvider provider, string payload, string functionName = "place-order") {
        var output = await provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(
                new MemoryStream(Encoding.UTF8.GetBytes(payload)),
                new Context(functionName));

        return new StreamReader(output).ReadToEnd();
    }

    [HardenedTest]
    public async Task ACallersPayloadReachesTheHandler(
        IServiceProvider provider, [Mock] IOrderLog log) {
        await Invoke(provider, """{"id":"o-1","quantity":3}""");

        log.Received().Placed(
            Arg.Is<OrderRequest>(request => request.Id == "o-1" && request.Quantity == 3));
    }

    /// <summary>
    /// The answer is the payload. Unlike every event source, a direct invocation has a caller
    /// waiting and the handler's return value is what they receive.
    /// </summary>
    [HardenedTest]
    public async Task TheHandlersReturnValueIsTheResponse(
        IServiceProvider provider, [Mock] IOrderLog log) {
        var response = await Invoke(provider, """{"id":"o-1","quantity":3}""");

        using var document = JsonDocument.Parse(response);

        Assert.Equal("o-1", document.RootElement.GetProperty("id").GetString());
        Assert.Equal("placed", document.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// The reason this family gets a function of its own. Every one of these would be claimed by an
    /// event adapter if one were registered here, and every one is legitimately the caller's.
    /// </summary>
    [HardenedTest]
    [InlineData("""{"id":"o-1","records":["a","b"]}""")]
    [InlineData("""{"id":"o-1","requestContext":"from-billing"}""")]
    [InlineData("""{"id":"o-1","records":["a"],"requestContext":"from-billing"}""")]
    public async Task APayloadShapedLikeAnAwsEventIsStillTheCallers(
        string payload, IServiceProvider provider, [Mock] IOrderLog log) {
        await Invoke(provider, payload);

        log.Received().Placed(Arg.Is<OrderRequest>(request => request.Id == "o-1"));
    }

    /// <summary>
    /// An unnamed handler answers whatever the deployment called the function, so the handler is
    /// not tied to a name that belongs to the infrastructure.
    /// </summary>
    [HardenedTest]
    [InlineData("place-order")]
    [InlineData("orders-prod-PlaceOrderFunction-A1B2C3")]
    public async Task TheHandlerAnswersWhateverTheFunctionIsCalled(
        string functionName, IServiceProvider provider, [Mock] IOrderLog log) {
        await Invoke(provider, """{"id":"o-1","quantity":1}""", functionName);

        log.Received().Placed(Arg.Any<OrderRequest>());
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

    private sealed class Context : ILambdaContext {
        public Context(string functionName) {
            FunctionName = functionName;
        }

        public string FunctionName { get; }

        public string AwsRequestId => "integration";
        public IClientContext ClientContext => null!;
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:place-order";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/place-order";
        public string LogStreamName => "stream";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
