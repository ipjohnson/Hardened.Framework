using System.Text.Json;
using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.LambdaRuntime.Tests;

/// <summary>
/// A Hardened application served over the Lambda Runtime API, which is the one path nothing else
/// covers.
///
/// <para>
/// Every other fixture reaches the handler through a façade or the invocation loop. That proves the
/// adapters, the pipeline and the routing, and skips what a deployed function actually does: poll
/// for work, read the deadline off the response headers, and post an answer or a failure back. This
/// runs <see cref="HardenedLambdaBootstrap"/> against a real socket speaking the real protocol.
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
    /// <c>AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE</c> is the runtime client's own switch for serving a
    /// single invocation and returning, which is what lets this be an ordinary awaited call rather
    /// than a background loop the test has to chase. The cancellation token is a deadline for the
    /// test itself: without one, a protocol mistake hangs the suite instead of failing it.
    /// </remarks>
    private static async Task<RuntimeApiStub.Answer> Serve(IServiceProvider provider, string payload) {
        using var runtime = new RuntimeApiStub(payload);

        Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", runtime.Address);
        Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE", "true");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "orders-function");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_MEMORY_SIZE", "512");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_VERSION", "$LATEST");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_GROUP_NAME", "/aws/lambda/orders-function");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_STREAM_NAME", "stream");

        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await HardenedLambdaBootstrap.Run(provider, giveUp.Token);

        return await runtime.Answered.WaitAsync(giveUp.Token);
    }

    /// <summary>
    /// The whole path: poll, adapt, route, bind, handle, answer. Nothing here names an adapter, a
    /// filter or a handler - it speaks the protocol AWS speaks and checks what came back.
    /// </summary>
    [HardenedTest]
    public async Task AnInvocationOverTheRuntimeApiReachesTheHandler(
        IServiceProvider provider, [Mock] IOrderStore store) {
        var answer = await Serve(provider, OneOrder);

        Assert.False(answer.Failed);

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 4));
    }

    /// <summary>The batch report is what goes back on the wire, not just what the adapter can produce.</summary>
    [HardenedTest]
    public async Task TheBatchReportIsWhatThePostContains(
        IServiceProvider provider, [Mock] IOrderStore store) {
        var answer = await Serve(provider, OneOrder);

        using var report = JsonDocument.Parse(answer.Body);

        Assert.Empty(report.RootElement.GetProperty("batchItemFailures").EnumerateArray());
    }

    /// <summary>
    /// The failure policy arriving where it matters. An event adapter rethrows, and a rethrow has
    /// to reach AWS as a posted invocation error - that is what returns the message to the queue.
    /// Answering with a 200 and a body describing the failure would tell SQS to delete it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedHandlerPostsAnInvocationError(
        IServiceProvider provider, [Mock] IOrderStore store) {
        store.When(one => one.Place(Arg.Any<Order>()))
            .Do(_ => throw new InvalidOperationException("handler refused the order"));

        var answer = await Serve(provider, OneOrder);

        Assert.True(answer.Failed);
        Assert.Contains("handler refused the order", answer.Body);
    }
}
