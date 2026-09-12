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

    /// <summary>
    /// A MODIFY on a table with NEW_AND_OLD_IMAGES, carrying every attribute type that has its own
    /// wire shape - so the unmarshaller is exercised by the fixture rather than only by its own
    /// unit tests.
    /// </summary>
    public const string DynamoDbJson = """
        {"Records":[{
          "eventID":"c81e728d9d4c2f636f067f89cc14862c",
          "eventName":"MODIFY",
          "eventVersion":"1.1",
          "eventSource":"aws:dynamodb",
          "awsRegion":"us-east-1",
          "dynamodb":{
            "ApproximateCreationDateTime":1767225600,
            "Keys":{"id":{"S":"order-1"}},
            "NewImage":{
              "id":{"S":"order-1"},
              "total":{"N":"42.5"},
              "paid":{"BOOL":true},
              "cancelled":{"NULL":true},
              "tags":{"SS":["rush","gift"]},
              "sizes":{"NS":["1","2"]},
              "lines":{"L":[{"S":"a"},{"N":"7"}]},
              "shipping":{"M":{"city":{"S":"Leeds"}}}
            },
            "OldImage":{"id":{"S":"order-1"},"total":{"N":"10"}},
            "SequenceNumber":"4421584500000000017450439091",
            "SizeBytes":112,
            "StreamViewType":"NEW_AND_OLD_IMAGES"
          },
          "eventSourceARN":"arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-01-01T00:00:00.000"
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

    /// <summary>
    /// Two records off one shard, with the arrival timestamp Kinesis actually sends - a number with
    /// a fraction, which is the shape that breaks a model typing it as a DateTime.
    /// </summary>
    /// <remarks>
    /// The data is base64 of <c>{"id":"a-1","quantity":7}</c> and <c>{"id":"a-2","quantity":9}</c>.
    /// Kinesis says nothing about what a publisher puts in a record, so what a handler binds is
    /// exactly these bytes.
    /// </remarks>
    public const string KinesisJson = """
        {"Records":[{
          "eventSource":"aws:kinesis","eventVersion":"1.0",
          "eventID":"shardId-000000000000:49590",
          "eventSourceARN":"arn:aws:kinesis:us-east-1:123456789012:stream/orders",
          "awsRegion":"us-east-1",
          "kinesis":{"kinesisSchemaVersion":"1.0","partitionKey":"p1","sequenceNumber":"49590",
            "data":"eyJpZCI6ImEtMSIsInF1YW50aXR5Ijo3fQ==",
            "approximateArrivalTimestamp":1545084650.987}},{
          "eventSource":"aws:kinesis","eventVersion":"1.0",
          "eventID":"shardId-000000000000:49591",
          "eventSourceARN":"arn:aws:kinesis:us-east-1:123456789012:stream/orders",
          "awsRegion":"us-east-1",
          "kinesis":{"kinesisSchemaVersion":"1.0","partitionKey":"p1","sequenceNumber":"49591",
            "data":"eyJpZCI6ImEtMiIsInF1YW50aXR5Ijo5fQ==",
            "approximateArrivalTimestamp":1545084651.500}}]}
        """;

    /// <summary>
    /// Two notifications on one bucket: a put and a delete.
    /// </summary>
    /// <remarks>
    /// The key is URL-encoded, as S3 sends it - <c>my+report.pdf</c> is an object actually named
    /// "my report.pdf". A delete carries no size and no etag, which is why both are nullable.
    /// </remarks>
    public const string S3Json = """
        {"Records":[{
          "eventVersion":"2.1","eventSource":"aws:s3","awsRegion":"us-east-1",
          "eventTime":"2026-01-01T00:00:00.000Z","eventName":"ObjectCreated:Put",
          "s3":{"s3SchemaVersion":"1.0","configurationId":"uploads",
            "bucket":{"name":"uploads","arn":"arn:aws:s3:::uploads"},
            "object":{"key":"my+report.pdf","size":1024,"eTag":"d41d8cd98f00b204e9800998ecf8427e",
              "sequencer":"00659A1B2C3D4E5F60"}}},{
          "eventVersion":"2.1","eventSource":"aws:s3","awsRegion":"us-east-1",
          "eventTime":"2026-01-01T00:00:01.000Z","eventName":"ObjectRemoved:Delete",
          "s3":{"s3SchemaVersion":"1.0","configurationId":"uploads",
            "bucket":{"name":"uploads","arn":"arn:aws:s3:::uploads"},
            "object":{"key":"old.txt","sequencer":"00659A1B2C3D4E5F61"}}}]}
        """;

    public const string FirehoseJson = """
        {"invocationId":"invoked123","deliveryStreamArn":"arn:aws:firehose:us-east-1:123:deliverystream/orders",
         "region":"us-east-1","records":[{"recordId":"r1","data":"aGVsbG8="}]}
        """;

    public const string HttpJson = """
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
