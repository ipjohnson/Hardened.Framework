using System.Text;
using Amazon.Lambda.SNSEvents;
using Amazon.Lambda.SQSEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Xunit;
using Hardened.Aws.Lambda.EventBridge;
using Hardened.Aws.Lambda.Sns;
using Hardened.Aws.Lambda.Sqs;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// What the three event adapters make of a payload once they have claimed it: the route, the body,
/// and what a per-record fork carries.
/// </summary>
public class EventSourceAdapterTests {
    private static string Read(Stream body) {
        using var reader = new StreamReader(body, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ SQS

    /// <summary>
    /// The queue name off the ARN, which is the whole of how a message finds its handler.
    /// </summary>
    /// <remarks>
    /// Built from real wire JSON on purpose. The field is <c>eventSourceARN</c> with the acronym
    /// capitalised, which no naming policy produces from <c>EventSourceArn</c> - so this fails if
    /// the DTO stops carrying its own property name, and the route would silently become "/".
    /// </remarks>
    [Fact]
    public void SqsRoutesOnTheQueueName() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        var request = new SqsAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("QUEUE", request.Method);
        Assert.Equal("/orders-new", request.Path);
    }

    [Fact]
    public void SqsSurfacesTheBatchForForking() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        var request = Assert.IsType<SqsRequest>(
            new SqsAdapter().CreateRequest(payload, TestLambdaContext.Instance));

        var record = Assert.Single(request.Records);

        Assert.Equal("11d6ee51-4cc7-4302-9e22-7cd8afdaadf5", record.MessageId);
    }

    /// <summary>
    /// A fork is one message: its body is the message body, not the batch.
    /// </summary>
    [Fact]
    public void ASqsForkCarriesTheMessageRatherThanTheBatch() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        var batch = (SqsRequest)new SqsAdapter().CreateRequest(payload, TestLambdaContext.Instance);
        var fork = batch.ForRecord(batch.Records[0]);

        Assert.Equal("""{"id":1}""", Read(fork.Body));
        Assert.Equal("QUEUE", fork.Method);
        Assert.Equal("/orders-new", fork.Path);
    }

    [Fact]
    public void ASqsForkCarriesMessageAttributesAsHeaders() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        var batch = (SqsRequest)new SqsAdapter().CreateRequest(payload, TestLambdaContext.Instance);
        var fork = batch.ForRecord(batch.Records[0]);

        Assert.Equal("abc-123", fork.Headers["trace"]);
        Assert.Equal("11d6ee51-4cc7-4302-9e22-7cd8afdaadf5", fork.Headers[SqsRequest.MessageIdHeader]);
        Assert.Equal(
            "arn:aws:sqs:us-east-1:123456789012:orders-new",
            fork.Headers[SqsRequest.QueueArnHeader]);
    }

    /// <summary>
    /// A message attribute cannot shadow the message id, which is why the three SQS facts are
    /// prefixed and a user's attributes are not.
    /// </summary>
    [Fact]
    public void AUserAttributeCannotShadowTheMessageId() {
        using var payload = Payloads.Sqs(new SQSEvent.SQSMessage {
            MessageId = "real",
            EventSourceArn = "arn:aws:sqs:us-east-1:123456789012:orders-new",
            Body = "{}",
            MessageAttributes = new Dictionary<string, SQSEvent.MessageAttribute> {
                ["x-amz-sqs-message-id"] = new() { StringValue = "spoofed", DataType = "String" }
            }
        });

        var batch = (SqsRequest)new SqsAdapter().CreateRequest(payload, TestLambdaContext.Instance);
        var fork = batch.ForRecord(batch.Records[0]);

        Assert.Equal("real", fork.Headers[SqsRequest.MessageIdHeader]);
    }

    /// <summary>
    /// Every message succeeded, which is the only report an invocation reaching here can make: the
    /// event family rethrows, so a failed handler fails the invocation instead.
    /// </summary>
    [Fact]
    public async Task SqsReportsAnEmptyFailureList() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        var adapter = new SqsAdapter();
        var batch = adapter.CreateRequest(payload, TestLambdaContext.Instance);
        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), batch), output);

        Assert.Equal("""{"batchItemFailures":[]}""", Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// A record with no usable ARN produces a route no handler declared, rather than an exception
    /// inside the adapter. The value is kept where there is one, so the not-found report names what
    /// actually arrived.
    /// </summary>
    /// <summary>
    /// The ids the filter recorded reach the report, keyed the way SQS expects.
    /// </summary>
    [Fact]
    public async Task AFailedMessageIsNamedInTheReport() {
        using var payload = Payloads.Sqs(
            new SQSEvent.SQSMessage { MessageId = "m0", Body = "{}", EventSourceArn = "arn:aws:sqs:r:a:q" },
            new SQSEvent.SQSMessage { MessageId = "m1", Body = "{}", EventSourceArn = "arn:aws:sqs:r:a:q" });

        var adapter = new SqsAdapter(reportsItemFailures: true);
        var batch = (SqsRequest)adapter.CreateRequest(payload, TestLambdaContext.Instance);

        batch.RecordFailure(1, new InvalidOperationException("no"));

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), batch), output);

        Assert.Equal(
            """{"batchItemFailures":[{"itemIdentifier":"m1"}]}""",
            Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// Off unless the deployment turned it on at the other end. A report sent to a mapping without
    /// ReportBatchItemFailures is discarded and every failed message is marked handled, so the
    /// default has to be the one that fails the invocation instead.
    /// </summary>
    [Fact]
    public void PartialBatchFailuresAreOffUnlessAskedFor() {
        using var payload = Payloads.Payload(Payloads.SqsJson);

        Assert.False(((SqsRequest)new SqsAdapter()
            .CreateRequest(payload, TestLambdaContext.Instance)).ReportsItemFailures);

        Assert.True(((SqsRequest)new SqsAdapter(reportsItemFailures: true)
            .CreateRequest(payload, TestLambdaContext.Instance)).ReportsItemFailures);
    }

    /// <summary>
    /// SNS has no per-notification report, so a failure there must fail the invocation. Recording
    /// one is a programming error rather than a silent no-op.
    /// </summary>
    [Fact]
    public void SnsRefusesToRecordAnItemFailure() {
        using var payload = Payloads.Payload(Payloads.SnsJson);

        var delivery = (SnsRequest)new SnsAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.False(delivery.ReportsItemFailures);
        Assert.Throws<NotSupportedException>(() => delivery.RecordFailure(0, new Exception()));
    }

    [Fact]
    public void AMalformedArnBecomesARouteNobodyDeclared() {
        Assert.Equal("notanarn", SqsAdapter.QueueName("notanarn"));
        Assert.Equal("", SqsAdapter.QueueName(null));
        Assert.Equal("", SqsAdapter.QueueName(""));
    }

    // ------------------------------------------------------------------ SNS

    [Fact]
    public void SnsRoutesOnTheTopicName() {
        using var payload = Payloads.Payload(Payloads.SnsJson);

        var request = new SnsAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("TOPIC", request.Method);
        Assert.Equal("/order-events", request.Path);
    }

    /// <summary>
    /// The topic ARN inside the notification, not the subscription ARN beside it - the latter has a
    /// further segment holding the subscription id, so reading it would route every subscriber to a
    /// different path.
    /// </summary>
    [Fact]
    public void SnsRoutesOnTheTopicNotTheSubscription() {
        using var payload = Payloads.Payload(Payloads.SnsJson);

        var request = new SnsAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.DoesNotContain("0b6941f8", request.Path);
    }

    [Fact]
    public void AnSnsForkCarriesThePublishedMessageRatherThanTheEnvelope() {
        using var payload = Payloads.Payload(Payloads.SnsJson);

        var batch = (SnsRequest)new SnsAdapter().CreateRequest(payload, TestLambdaContext.Instance);
        var fork = batch.ForRecord(batch.Records[0]);

        Assert.Equal("""{"id":1}""", Read(fork.Body));
        Assert.Equal("Order placed", fork.Headers[SnsRequest.SubjectHeader]);
        Assert.Equal("abc-123", fork.Headers["trace"]);
    }

    // ------------------------------------------------------------------ EventBridge

    [Fact]
    public void AnEventRoutesOnItsSourceAndDetailType() {
        using var payload = Payloads.Payload(Payloads.EventBridgeJson);

        var request = new EventBridgeAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("EVENT", request.Method);
        Assert.Equal("/com.acme.orders/OrderPlaced", request.Path);
    }

    /// <summary>
    /// A schedule is the same envelope with AWS's own source, and it routes on the rule rather than
    /// on "aws.events/Scheduled Event" - which every schedule in the account would share.
    /// </summary>
    [Fact]
    public void AScheduleRoutesOnTheRuleName() {
        using var payload = Payloads.Payload(Payloads.ScheduledJson);

        var request = new EventBridgeAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("TIMER", request.Method);
        Assert.Equal("/nightly-rollup", request.Path);
    }

    /// <summary>
    /// The detail is the body, so a handler binds what was published rather than the envelope AWS
    /// wrapped it in. This is the reason the adapter binds no DTO: the detail's type is the
    /// handler's to choose.
    /// </summary>
    [Fact]
    public void TheDetailIsTheBody() {
        using var payload = Payloads.Payload(Payloads.EventBridgeJson);

        var request = new EventBridgeAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("""{"id":1,"total":42}""", Read(request.Body));
    }

    [Fact]
    public void TheEnvelopeIsSurfacedAsHeaders() {
        using var payload = Payloads.Payload(Payloads.EventBridgeJson);

        var request = new EventBridgeAdapter().CreateRequest(payload, TestLambdaContext.Instance);

        Assert.Equal("7bf73129-1428-4cd3-a780-95db273d1602",
            request.Headers[EventBridgeAdapter.EventIdHeader]);
        Assert.Equal("com.acme.orders", request.Headers[EventBridgeAdapter.EventSourceHeader]);
        Assert.Equal("OrderPlaced", request.Headers[EventBridgeAdapter.EventDetailTypeHeader]);
    }
}
