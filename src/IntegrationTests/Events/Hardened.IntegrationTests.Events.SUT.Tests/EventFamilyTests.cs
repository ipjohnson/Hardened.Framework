using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// One function serving a queue, a topic, a schedule and a bus.
///
/// <para>
/// The SQS fixture holds the single-adapter case, where nothing is ever asked about a payload.
/// This one holds the other half of the family split: several sources sharing a function, so every
/// invocation goes through the peek. SQS and SNS both arrive as a <c>Records</c> array, which is
/// where an adapter matching the array rather than the event source value would claim both and the
/// first registered would win.
/// </para>
/// </summary>
public class EventFamilyTests : IDisposable {
    private readonly ServiceProvider _provider;

    private readonly RecordingTriggerLog _log = new();

    public EventFamilyTests() {
        _provider = new EventsTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            (_, services) => services.AddSingleton<ITriggerLog>(_log),
            builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private Task<Stream> Invoke(string payload) =>
        _provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), new InvocationContext());

    // ------------------------------------------------------------------ payloads

    private const string Queued = """
        {"Records":[{
          "messageId":"m0","receiptHandle":"r0",
          "body":"{\"id\":\"q-1\",\"quantity\":1}",
          "eventSource":"aws:sqs",
          "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
          "awsRegion":"us-east-1"
        }]}
        """;

    private const string Published = """
        {"Records":[{
          "EventVersion":"1.0",
          "EventSubscriptionArn":"arn:aws:sns:us-east-1:123456789012:order-events:0b6941f8",
          "EventSource":"aws:sns",
          "Sns":{
            "Type":"Notification",
            "MessageId":"95df01b4",
            "TopicArn":"arn:aws:sns:us-east-1:123456789012:order-events",
            "Message":"{\"id\":\"t-1\",\"quantity\":2}",
            "Timestamp":"2026-09-07T12:00:00.000Z"
          }
        }]}
        """;

    private const string Scheduled = """
        {
          "version":"0","id":"cdc73f9d","detail-type":"Scheduled Event","source":"aws.events",
          "account":"123456789012","time":"2026-09-07T02:00:00Z","region":"us-east-1",
          "resources":["arn:aws:events:us-east-1:123456789012:rule/nightly-rollup"],
          "detail":{}
        }
        """;

    private const string Placed = """
        {
          "version":"0","id":"7bf73129","detail-type":"OrderPlaced","source":"com.acme.orders",
          "account":"123456789012","time":"2026-09-07T12:00:00Z","region":"us-east-1",
          "resources":[],
          "detail":{"id":"e-1","quantity":5}
        }
        """;

    // ------------------------------------------------------------------ each source finds its own

    [Fact]
    public async Task ANotificationReachesTheTopicHandler() {
        await Invoke(Published);

        Assert.Equal(["topic:t-1"], _log.Entries);
    }

    /// <summary>
    /// The body is the published message, not the notification envelope, so a handler binding its
    /// own type gets what the publisher sent.
    /// </summary>
    [Fact]
    public async Task ANotificationBindsThePublishedMessage() {
        await Invoke(Published);

        Assert.Contains("t-1", _log.Entries[0]);
    }

    /// <summary>
    /// A schedule routes on its rule rather than on aws.events/Scheduled Event, which every
    /// schedule in the account would share.
    /// </summary>
    [Fact]
    public async Task AScheduledInvocationReachesTheTimerHandler() {
        await Invoke(Scheduled);

        Assert.Equal(["timer:nightly-rollup"], _log.Entries);
    }

    /// <summary>
    /// The same envelope as a schedule, told apart by its source, and bound from its detail rather
    /// than the envelope around it.
    /// </summary>
    [Fact]
    public async Task ABusEventReachesTheEventHandlerAndBindsItsDetail() {
        await Invoke(Placed);

        Assert.Equal(["event:e-1"], _log.Entries);
    }

    [Fact]
    public async Task AQueueMessageStillReachesTheQueueHandler() {
        await Invoke(Queued);

        Assert.Equal(["queue:q-1"], _log.Entries);
    }

    // ------------------------------------------------------------------ the peek

    /// <summary>
    /// Four triggers, three adapters: EventBridge serves both the schedule and the bus event, and
    /// registering it twice would put two adapters in front of every EventBridge payload.
    /// </summary>
    [Fact]
    public void FourTriggersRegisterThreeAdapters() {
        var adapters = _provider.GetServices<IPayloadAdapter>().ToArray();

        Assert.Equal(3, adapters.Length);
        Assert.Contains(adapters, adapter => adapter is SqsAdapter);
        Assert.Contains(adapters, adapter => adapter is SnsAdapter);
        Assert.Contains(adapters, adapter => adapter is EventBridgeAdapter);
    }

    /// <summary>
    /// The assertion the family split rests on, run through a real application rather than against
    /// the adapters directly: four payloads, four different handlers, one function.
    /// </summary>
    [Fact]
    public async Task EverySourceReachesItsOwnHandlerInOneFunction() {
        await Invoke(Queued);
        await Invoke(Published);
        await Invoke(Scheduled);
        await Invoke(Placed);

        Assert.Equal(
            ["queue:q-1", "topic:t-1", "timer:nightly-rollup", "event:e-1"],
            _log.Entries);
    }

    /// <summary>
    /// A source the deployment wired that no handler asked for. Guessing would hand it to code
    /// written for another shape, so the invocation fails and names what the function serves.
    /// </summary>
    [Fact]
    public async Task APayloadNoAdapterClaimsFailsTheInvocation() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke("""{"Records":[{"eventSource":"aws:kinesis","kinesis":{"data":"aGk="}}]}"""));

        Assert.Contains("SqsAdapter", failure.Message);
        Assert.Empty(_log.Entries);
    }

    private sealed class InvocationContext : ILambdaContext {
        public string AwsRequestId => "integration";
        public IClientContext ClientContext => null!;
        public string FunctionName => "events-function";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:events";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/events";
        public string LogStreamName => "stream";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
