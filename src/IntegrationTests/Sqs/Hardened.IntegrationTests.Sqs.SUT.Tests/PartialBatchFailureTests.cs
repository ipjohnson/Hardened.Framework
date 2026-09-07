using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// The same handlers under a mapping deployed with <c>ReportBatchItemFailures</c>.
///
/// <para>
/// The pair of applications is the assertion. <c>SqsTestApp</c> and <c>PartialFailureApp</c> serve
/// identical handlers and differ only in whether the deployment said it reports individual
/// failures, so what changes between these tests and <see cref="QueueFunctionTests"/> is entirely
/// the failure policy - not the code, not the payload, not the route.
/// </para>
/// </summary>
[Collection(QueueHandlerState.Name)]
public class PartialBatchFailureTests : IDisposable {
    private readonly ServiceProvider _provider;

    public PartialBatchFailureTests() {
        OrderHandlers.Reset();

        _provider = new PartialFailureApp().CreateServiceProvider(
            new EnvironmentImpl(null), null, builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private async Task<string[]> Invoke(params (string Id, bool Fails)[] orders) {
        foreach (var order in orders.Where(order => order.Fails)) {
            OrderHandlers.FailFor.Add(order.Id);
        }

        var records = orders.Select((order, index) => $$"""
            {
              "messageId":"m-{{order.Id}}",
              "receiptHandle":"r{{index}}",
              "body":"{\"id\":\"{{order.Id}}\",\"quantity\":1}",
              "eventSource":"aws:sqs",
              "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
              "awsRegion":"us-east-1"
            }
            """);

        var payload = "{\"Records\":[" + string.Join(",", records) + "]}";

        var output = await _provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), new InvocationContext());

        using var report = JsonDocument.Parse(new StreamReader(output).ReadToEnd());

        return report.RootElement.GetProperty("batchItemFailures")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("itemIdentifier").GetString()!)
            .ToArray();
    }

    /// <summary>
    /// Every message succeeded. An empty list is the report, not an absent one: SQS reads it as
    /// "delete all of them".
    /// </summary>
    [Fact]
    public async Task ASuccessfulBatchReportsNoFailures() {
        Assert.Empty(await Invoke(("a-1", false), ("a-2", false)));
    }

    /// <summary>
    /// The invocation succeeds and names the one message that did not. Without this the whole batch
    /// would be redelivered, including the messages that were handled.
    /// </summary>
    [Fact]
    public async Task OnlyTheFailedMessageIsReported() {
        Assert.Equal(["m-a-2"], await Invoke(("a-1", false), ("a-2", true), ("a-3", false)));
    }

    /// <summary>
    /// Stopping at the first failure would leave the rest unhandled and unreported, and SQS would
    /// delete them as successfully processed.
    /// </summary>
    [Fact]
    public async Task EveryMessageIsStillAttemptedAfterOneFails() {
        var failures = await Invoke(("a-1", true), ("a-2", false), ("a-3", false));

        Assert.Equal(["m-a-1"], failures);
        Assert.Equal(["a-1", "a-2", "a-3"], OrderHandlers.Handled.Select(order => order.Id));
    }

    [Fact]
    public async Task SeveralFailuresAreAllReported() {
        Assert.Equal(
            ["m-a-1", "m-a-3"],
            await Invoke(("a-1", true), ("a-2", false), ("a-3", true)));
    }

    /// <summary>
    /// Every message failing is still a successful invocation with a full report, rather than a
    /// throw. Failing here would redeliver the batch through the invocation-level path as well,
    /// which is the same messages counted twice against the redrive policy.
    /// </summary>
    [Fact]
    public async Task AWhollyFailedBatchReportsEveryMessage() {
        Assert.Equal(["m-a-1", "m-a-2"], await Invoke(("a-1", true), ("a-2", true)));
    }

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
