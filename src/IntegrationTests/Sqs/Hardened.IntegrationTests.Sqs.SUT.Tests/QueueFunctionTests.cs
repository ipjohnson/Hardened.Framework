using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Sqs.Shared;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// A queue function, whole: the trigger attribute, the generator, the module the build property
/// named, the adapter, the batch filter and the invocation loop, in one compilation.
///
/// <para>
/// Every part of this is unit-tested somewhere else. This is the only place they have to agree with
/// each other - that the scheme the generator registers a route under is the one the adapter puts
/// on a request, that the module the property names is the one holding that adapter, and that a
/// handler declared with nothing but <c>[Queue]</c> is reached at all.
/// </para>
/// </summary>
public class QueueFunctionTests : IDisposable {
    private readonly ServiceProvider _provider;
    private readonly RecordingOrderStore _store = new();

    public QueueFunctionTests() {
        // The store is this test's own, so nothing is shared between fixtures and nothing has to be
        // reset. The static list this replaced had two test classes overwriting each other.
        _provider = new SqsTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            (_, services) => services.AddSingleton<IOrderStore>(_store),
            builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private Task<Stream> Invoke(string payload) =>
        _provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), new InvocationContext());

    private static string Batch(params (string Id, int Quantity)[] orders) {
        var records = orders.Select((order, index) => $$"""
            {
              "messageId":"m{{index}}",
              "receiptHandle":"r{{index}}",
              "body":"{\"id\":\"{{order.Id}}\",\"quantity\":{{order.Quantity}}}",
              "eventSource":"aws:sqs",
              "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
              "awsRegion":"us-east-1"
            }
            """);

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    /// <summary>
    /// The claim the whole design rests on: a handler that names a queue and nothing else is
    /// reached by a message from that queue.
    /// </summary>
    [Fact]
    public async Task AQueueMessageReachesTheHandler() {
        await Invoke(Batch(("a-1", 2)));

        var order = Assert.Single(_store.Placed);

        Assert.Equal("a-1", order.Id);
        Assert.Equal(2, order.Quantity);
    }

    /// <summary>
    /// One invocation, one handler call per message. The route was chosen once from the queue the
    /// batch arrived against; the fan-out is the filter.
    /// </summary>
    [Fact]
    public async Task EveryMessageInABatchIsHandledSeparately() {
        await Invoke(Batch(("a-1", 1), ("a-2", 2), ("a-3", 3)));

        Assert.Equal(["a-1", "a-2", "a-3"], _store.Placed.Select(order => order.Id));
    }

    /// <summary>
    /// Each fork binds its own record's body, so a handler sees what was published rather than the
    /// batch it arrived in.
    /// </summary>
    [Fact]
    public async Task EachMessageBindsItsOwnBody() {
        await Invoke(Batch(("a-1", 10), ("a-2", 20)));

        Assert.Equal([10, 20], _store.Placed.Select(order => order.Quantity));
    }

    /// <summary>
    /// The adapter is registered because a handler wrote <c>[Queue]</c> - nothing in the SUT names
    /// SQS - and it is the only one, so the invocation loop never peeks at a payload.
    /// </summary>
    [Fact]
    public void TheTriggerRegisteredTheAdapter() {
        var adapter = Assert.Single(_provider.GetServices<IPayloadAdapter>());

        Assert.IsType<SqsAdapter>(adapter);
    }

    /// <summary>
    /// Failing the invocation is what returns a message to the queue. With
    /// ReportBatchItemFailures off - the default, because a report sent to a mapping that did not
    /// ask for one is discarded - a failed message has to take the whole batch with it.
    /// </summary>
    [Fact]
    public async Task AFailedMessageFailsTheInvocation() {
        _store.Refusing("a-2");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke(Batch(("a-1", 1), ("a-2", 2))));
    }

    /// <summary>Enough context to invoke, with a deadline the host turns into a token.</summary>
    private sealed class InvocationContext : ILambdaContext {
        public string AwsRequestId => "integration";
        public IClientContext ClientContext => null!;
        public string FunctionName => "orders-function";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:orders";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/orders";
        public string LogStreamName => "stream";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
