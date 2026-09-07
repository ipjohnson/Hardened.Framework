using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Invoke.SUT.Tests;

/// <summary>
/// A directly invoked function: whatever the caller sent, and an answer back.
///
/// <para>
/// The family that must not share a function with the others, and this is where that stops being an
/// argument. The payload here carries fields called <c>Records</c> and <c>requestContext</c> - the
/// fields SQS, SNS, streams and API Gateway are recognised by - because a caller is entitled to
/// them and nothing can tell them from AWS's by inspection.
/// </para>
/// </summary>
public class DirectInvokeTests : IDisposable {
    private readonly ServiceProvider _provider;

    private readonly RecordingOrderLog _log = new();

    public DirectInvokeTests() {
        _provider = new InvokeTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            (_, services) => services.AddSingleton<IOrderLog>(_log),
            builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private async Task<string> Invoke(string payload, string functionName = "place-order") {
        var output = await _provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(
                new MemoryStream(Encoding.UTF8.GetBytes(payload)),
                new InvocationContext(functionName));

        return new StreamReader(output).ReadToEnd();
    }

    [Fact]
    public async Task ACallersPayloadReachesTheHandler() {
        await Invoke("""{"id":"o-1","quantity":3}""");

        var request = Assert.Single(_log.Requests);

        Assert.Equal("o-1", request.Id);
        Assert.Equal(3, request.Quantity);
    }

    /// <summary>
    /// The answer is the payload. Unlike every event source, a direct invocation has a caller
    /// waiting on the other end and the handler's return value is what they receive.
    /// </summary>
    [Fact]
    public async Task TheHandlersReturnValueIsTheResponse() {
        var response = await Invoke("""{"id":"o-1","quantity":3}""");

        using var document = JsonDocument.Parse(response);

        Assert.Equal("o-1", document.RootElement.GetProperty("id").GetString());
        Assert.Equal("placed", document.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// The reason this family gets a function of its own. Every one of these payloads would be
    /// claimed by an event adapter if one were registered here, and every one is legitimately the
    /// caller's - so the split is what keeps them from being handled as the wrong thing.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"o-1","records":["a","b"]}""")]
    [InlineData("""{"id":"o-1","requestContext":"from-billing"}""")]
    [InlineData("""{"id":"o-1","records":["a"],"requestContext":"from-billing"}""")]
    public async Task APayloadShapedLikeAnAwsEventIsStillTheCallers(string payload) {
        await Invoke(payload);

        Assert.Equal("o-1", Assert.Single(_log.Requests).Id);
    }

    /// <summary>
    /// An unnamed handler answers whatever the deployment called the function, so the handler is
    /// not tied to a name that belongs to the infrastructure.
    /// </summary>
    [Theory]
    [InlineData("place-order")]
    [InlineData("orders-prod-PlaceOrderFunction-A1B2C3")]
    public async Task TheHandlerAnswersWhateverTheFunctionIsCalled(string functionName) {
        await Invoke("""{"id":"o-1","quantity":1}""", functionName);

        Assert.Single(_log.Requests);
    }

    /// <summary>
    /// One adapter, so nothing is ever asked about a payload. That is required rather than an
    /// optimisation here: a caller may send anything, and asking would mean parsing something that
    /// need not be JSON.
    /// </summary>
    [Fact]
    public void TheInvokeAdapterIsTheOnlyOne() {
        Assert.IsType<InvokeAdapter>(Assert.Single(_provider.GetServices<IPayloadAdapter>()));
    }

    private sealed class InvocationContext : ILambdaContext {
        public InvocationContext(string functionName) {
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
