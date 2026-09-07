using System.Text;
using System.Text.Json;
using Amazon.Lambda.SNSEvents;
using Amazon.Lambda.SQSEvents;
using Hardened.Aws.Lambda.Runtime.Execution;

namespace Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// Event payloads, built the way AWS sends them.
/// </summary>
/// <remarks>
/// The discrimination fixtures are hand-written JSON rather than serialised DTOs, because what an
/// adapter has to recognise is the wire shape - and a DTO round trip would only prove the adapter
/// agrees with the same type it was built from. <c>eventSourceARN</c> in particular is capitalised
/// on the wire in a way no naming policy produces, so a fixture that went through the DTO would
/// hide a binding that does not work against a real event.
/// </remarks>
public static class Payloads {
    public const string SqsJson = """
        {"Records":[{
          "messageId":"11d6ee51-4cc7-4302-9e22-7cd8afdaadf5",
          "receiptHandle":"AQEBBX8nesZEXmkhsmZeyIE8iQAMig7qw...",
          "body":"{\"id\":1}",
          "attributes":{"ApproximateReceiveCount":"1","SentTimestamp":"1545082649183"},
          "messageAttributes":{"trace":{"stringValue":"abc-123","dataType":"String"}},
          "md5OfBody":"e4e68fb7bd0e697a0ae8f1bb342846b3",
          "eventSource":"aws:sqs",
          "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
          "awsRegion":"us-east-1"
        }]}
        """;

    public const string SnsJson = """
        {"Records":[{
          "EventVersion":"1.0",
          "EventSubscriptionArn":"arn:aws:sns:us-east-1:123456789012:order-events:0b6941f8",
          "EventSource":"aws:sns",
          "Sns":{
            "Type":"Notification",
            "MessageId":"95df01b4-ee98-5cb9-9903-4c221d41eb5e",
            "TopicArn":"arn:aws:sns:us-east-1:123456789012:order-events",
            "Subject":"Order placed",
            "Message":"{\"id\":1}",
            "Timestamp":"2026-09-06T12:00:00.000Z",
            "MessageAttributes":{"trace":{"Type":"String","Value":"abc-123"}}
          }
        }]}
        """;

    public const string EventBridgeJson = """
        {
          "version":"0",
          "id":"7bf73129-1428-4cd3-a780-95db273d1602",
          "detail-type":"OrderPlaced",
          "source":"com.acme.orders",
          "account":"123456789012",
          "time":"2026-09-06T12:00:00Z",
          "region":"us-east-1",
          "resources":[],
          "detail":{"id":1,"total":42}
        }
        """;

    public const string ScheduledJson = """
        {
          "version":"0",
          "id":"cdc73f9d-aea9-11e3-9d5a-835b769c0d9c",
          "detail-type":"Scheduled Event",
          "source":"aws.events",
          "account":"123456789012",
          "time":"2026-09-06T02:00:00Z",
          "region":"us-east-1",
          "resources":["arn:aws:events:us-east-1:123456789012:rule/nightly-rollup"],
          "detail":{}
        }
        """;

    public const string DynamoStreamJson = """
        {"Records":[{"eventID":"1","eventName":"INSERT","eventSource":"aws:dynamodb",
          "eventSourceARN":"arn:aws:dynamodb:us-east-1:123456789012:table/Orders/stream/2026",
          "dynamodb":{"Keys":{"id":{"S":"1"}}}}]}
        """;

    public const string KinesisJson = """
        {"Records":[{"eventSource":"aws:kinesis","eventID":"shardId-000000000000:49590",
          "eventSourceARN":"arn:aws:kinesis:us-east-1:123456789012:stream/orders",
          "kinesis":{"partitionKey":"p1","sequenceNumber":"49590","data":"aGVsbG8="}}]}
        """;

    public const string FirehoseJson = """
        {"invocationId":"invoked123","deliveryStreamArn":"arn:aws:firehose:us-east-1:123:deliverystream/orders",
         "region":"us-east-1","records":[{"recordId":"r1","data":"aGVsbG8="}]}
        """;

    public const string ApiGatewayJson = """
        {"version":"2.0","rawPath":"/orders","requestContext":{"http":{"method":"GET"}}}
        """;

    public static LambdaPayload Payload(string json) =>
        new(Encoding.UTF8.GetBytes(json));

    /// <summary>An SQS batch built from the DTO, for a test that needs to vary a record.</summary>
    public static LambdaPayload Sqs(params SQSEvent.SQSMessage[] records) =>
        new(JsonSerializer.SerializeToUtf8Bytes(
            new SQSEvent { Records = records.ToList() },
            TestSerializerContext.Default.SQSEvent));

    public static LambdaPayload Sns(params SNSEvent.SNSRecord[] records) =>
        new(JsonSerializer.SerializeToUtf8Bytes(
            new SNSEvent { Records = records.ToList() },
            TestSerializerContext.Default.SNSEvent));
}
