using System.Text.Json;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Sqs.Shared;
using Hardened.IntegrationTests.Sqs.SUT;
using Microsoft.Extensions.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Xunit;

namespace Hardened.IntegrationTests.LambdaRuntime.Tests;

/// <summary>
/// A Hardened application served over the Lambda Runtime API, which is the one path nothing else
/// covers.
///
/// <para>
/// Every other fixture calls <c>LambdaInvocationHandler</c> directly. That proves the adapters, the
/// pipeline and the routing, and it skips the whole of what a deployed function actually does:
/// resolve the handler out of the container, poll for work, read the deadline off the response
/// headers, and post an answer or a failure back. This runs
/// <see cref="HardenedLambdaBootstrap"/> against a real socket speaking the real protocol.
/// </para>
/// </summary>
public class LambdaRuntimeLoopTests {

    private const string OneOrder = """
        {"Records":[{
          "messageId":"m0","receiptHandle":"r0",
          "body":"{\"id\":\"a-1\",\"quantity\":4}",
          "eventSource":"aws:sqs",
          "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
          "awsRegion":"us-east-1"
        }]}
        """;

    /// <summary>
    /// Runs the bootstrap for exactly one invocation and returns what the function posted back.
    /// </summary>
    /// <remarks>
    /// The store comes back with the answer because it is this invocation's own. It used to be a
    /// static the helper reset on the way in, which silently discarded the failure a test had just
    /// arranged - the arrange ran before the reset did.
    ///
    /// <c>AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE</c> is the runtime client's own switch for serving a
    /// single invocation and returning, which is what lets this be an ordinary awaited call rather
    /// than a background loop the test has to chase. The cancellation token is a deadline for the
    /// test itself: without one, a protocol mistake hangs the suite instead of failing it.
    /// </remarks>
    private static async Task<(RuntimeApiStub.Answer Answer, RecordingOrderStore Store)> Serve(
        string payload, string? failFor = null) {
        var store = new RecordingOrderStore();

        if (failFor != null) {
            store.Refusing(failFor);
        }

        using var runtime = new RuntimeApiStub(payload);

        Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", runtime.Address);
        Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE", "true");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "orders-function");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_MEMORY_SIZE", "512");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_VERSION", "$LATEST");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_GROUP_NAME", "/aws/lambda/orders-function");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_STREAM_NAME", "stream");

        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var provider = new SqsTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            (_, services) => services.AddSingleton<IOrderStore>(store),
            builder => { });

        await HardenedLambdaBootstrap.Run(provider, giveUp.Token);

        return (await runtime.Answered.WaitAsync(giveUp.Token), store);
    }

    /// <summary>
    /// The whole path: poll, adapt, route, bind, handle, answer. Nothing in this test names an
    /// adapter, a filter or a handler - it speaks the protocol AWS speaks and checks what came back.
    /// </summary>
    [Fact]
    public async Task AnInvocationOverTheRuntimeApiReachesTheHandler() {
        var (answer, store) = await Serve(OneOrder);

        Assert.False(answer.Failed);

        var order = Assert.Single(store.Placed);

        Assert.Equal("a-1", order.Id);
        Assert.Equal(4, order.Quantity);
    }

    /// <summary>
    /// The batch report is what goes back on the wire, not just what the adapter can produce.
    /// </summary>
    [Fact]
    public async Task TheBatchReportIsWhatThePostContains() {
        var (answer, _) = await Serve(OneOrder);

        using var report = JsonDocument.Parse(answer.Body);

        Assert.Empty(report.RootElement.GetProperty("batchItemFailures").EnumerateArray());
    }

    /// <summary>
    /// The failure policy arriving where it matters. An event adapter rethrows, and a rethrow has
    /// to reach AWS as a posted invocation error - that is what returns the message to the queue.
    /// Answering with a 200 and a body describing the failure would tell SQS to delete it.
    /// </summary>
    [Fact]
    public async Task AFailedHandlerPostsAnInvocationError() {
        var (answer, _) = await Serve(OneOrder, failFor: "a-1");

        Assert.True(answer.Failed);
        Assert.Contains("handler refused order a-1", answer.Body);
    }
}
