using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.Rie.Tests;

/// <summary>
/// The event family fixture inside the Lambda base image: four sources, three adapters, and the
/// peek choosing between them in the real runtime.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class EventFamilyImageTests : IClassFixture<EventFamilyImageTests.Function> {
    private readonly Function _function;

    public EventFamilyImageTests(Function function) {
        _function = function;
    }

    private static Task<Observation> Observed(LambdaRuntimeInterfaceEmulator emulator, string entry) =>
        emulator.Observed.WaitFor(one => one.Has("entry", entry), cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task AQueueMessageReachesTheQueueHandler() {
        var result = await _function.Emulator.InvokeAsync("""
            {"Records":[{"messageId":"m-q-1","receiptHandle":"r","body":"{\"id\":\"q-1\",\"quantity\":1}",
              "eventSource":"aws:sqs","eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new","awsRegion":"us-east-1"}]}
            """, TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);
        await Observed(_function.Emulator, "queue:q-1");
    }

    [Fact]
    public async Task ANotificationReachesTheTopicHandler() {
        var result = await _function.Emulator.InvokeAsync("""
            {"Records":[{"EventVersion":"1.0","EventSource":"aws:sns",
              "EventSubscriptionArn":"arn:aws:sns:us-east-1:123456789012:order-events:sub-0",
              "Sns":{"Type":"Notification","MessageId":"t-1","TopicArn":"arn:aws:sns:us-east-1:123456789012:order-events",
                     "Message":"{\"id\":\"t-1\",\"quantity\":1}"}}]}
            """, TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);
        await Observed(_function.Emulator, "topic:t-1");
    }

    [Fact]
    public async Task AScheduledInvocationReachesTheTimerHandler() {
        var result = await _function.Emulator.InvokeAsync("""
            {"version":"0","id":"nightly-fired","detail-type":"Scheduled Event","source":"aws.events",
             "account":"123456789012","time":"2026-01-01T00:00:00Z","region":"us-east-1",
             "resources":["arn:aws:events:us-east-1:123456789012:rule/nightly-rollup"],"detail":{}}
            """, TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);
        await Observed(_function.Emulator, "timer:nightly-rollup");
    }

    [Fact]
    public async Task ABusEventReachesTheEventHandler() {
        var result = await _function.Emulator.InvokeAsync("""
            {"version":"0","id":"e-1-event","detail-type":"OrderPlaced","source":"com.acme.orders",
             "account":"123456789012","time":"2026-01-01T00:00:00Z","region":"us-east-1",
             "resources":[],"detail":{"id":"e-1","quantity":1}}
            """, TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);
        await Observed(_function.Emulator, "event:e-1");
    }

    public sealed class Function : IAsyncLifetime {
        public LambdaRuntimeInterfaceEmulator Emulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.RieEvents.SUT"),
            "Hardened.IntegrationTests.RieEvents.SUT");

        public async ValueTask InitializeAsync() => await Emulator.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Emulator.DisposeAsync();
    }
}
