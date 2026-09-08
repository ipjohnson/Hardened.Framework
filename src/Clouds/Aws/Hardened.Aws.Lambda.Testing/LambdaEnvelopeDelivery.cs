using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Turns a message and a route into the envelope AWS would have sent, and invokes.
/// </summary>
/// <remarks>
/// <para>
/// The higher-fidelity half of <see cref="ITriggerDelivery"/>. A test says
/// <c>SendTo.OrdersNew(order)</c> and what runs is the real payload through the real loop: the
/// adapter recognises it, the peek picks it where more than one is registered, the batch filter
/// forks it, the binder binds it. The neutral delivery covers everything from routing inwards; this
/// adds the parts only a provider knows - the envelope, the body encoding that source uses, and the
/// metadata it carries.
/// </para>
/// <para>
/// The envelopes are the wire shapes rather than serialized DTOs, for the reason the adapter tests
/// give: <c>eventSourceARN</c> is capitalised in a way no naming policy produces, so a fixture
/// built by round-tripping a DTO would agree with the type it came from and not with AWS.
/// </para>
/// </remarks>
public sealed class LambdaEnvelopeDelivery : ITriggerDelivery {
    private readonly Func<ValueTask<LambdaInvocationHandler>> _handler;
    private readonly string _region;
    private readonly string _account;

    public LambdaEnvelopeDelivery(
        LambdaInvocationHandler handler,
        string region = "us-east-1",
        string account = "123456789012") {
        _handler = () => new ValueTask<LambdaInvocationHandler>(handler);
        _region = region;
        _account = account;
    }

    /// <summary>
    /// The delivery the harness builds: a handler off a container of its own for every invocation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container per invocation, because an execution environment is not promised between them.
    /// Two queue handlers deployed as two functions are two processes, so a send that passed only
    /// because the previous one warmed a singleton is a test that cannot fail for the reason
    /// production will.
    /// </para>
    /// <para>
    /// The handler comes with the container rather than being held here, and that is the fidelity
    /// worth having: <c>LambdaInvocationHandler</c> installs dispatch on its first invocation and
    /// keeps a flag saying it has, so a fresh handler on a fresh container is a cold environment
    /// doing exactly what a cold environment does.
    /// </para>
    /// <para>
    /// A batch is one invocation and therefore one container. Three messages in one send arrive as
    /// one event carrying three records, and the fan-out to a handler call per message happens
    /// inside it.
    /// </para>
    /// </remarks>
    public LambdaEnvelopeDelivery(
        ITestContainerSource source,
        string region = "us-east-1",
        string account = "123456789012") {
        _handler = async () =>
            (await source.CreateAsync()).GetRequiredService<LambdaInvocationHandler>();
        _region = region;
        _account = account;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what the handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        var name = path.TrimStart('/');
        var items = messages;

        var payload = scheme switch {
            "QUEUE" => Sqs(name, items),
            "TOPIC" => Sns(name, items),
            "TIMER" => Scheduled(name),
            "CHANGE" => DynamoDb(name, items),
            "STREAM" => Kinesis(name, items),
            "BLOB" => S3(name, items),
            _ => throw new NotSupportedException(
                $"No test envelope is built for the {scheme} scheme yet. Queues, topics, timers, " +
                "changes, streams and blobs have one; events are addressed by source and detail type and need " +
                "their own shape.")
        };

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        await (await _handler()).Invoke(input, new TestContext(name));
    }

    /// <summary>
    /// One direct invocation: the caller's own bytes in, the handler's answer out.
    /// </summary>
    /// <remarks>
    /// No envelope, because a direct invocation has none - the payload is whatever the caller sent,
    /// which is the whole distinction that gives this family a function of its own. What the
    /// envelope path does add is the invocation loop and the invoke adapter, so the answer is read
    /// back out of the response stream exactly as a caller would receive it.
    /// </remarks>
    public async Task<object?> Call(object message, string scheme, string path, Type? responseType) {
        var payload = JsonSerializer.Serialize(message, Wire);

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var output = await (await _handler()).Invoke(input, new TestContext(path.TrimStart('/')));

        if (responseType == null) {
            return null;
        }

        return await JsonSerializer.DeserializeAsync(output, responseType, Wire);
    }

    private string Sqs(string queue, System.Collections.IEnumerable messages) {
        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var body = JsonSerializer.Serialize(message, Wire);

            records.Add($$"""
                {"messageId":"{{queue}}-{{index}}","receiptHandle":"receipt-{{index}}",
                 "body":{{JsonSerializer.Serialize(body)}},
                 "eventSource":"aws:sqs",
                 "eventSourceARN":"arn:aws:sqs:{{_region}}:{{_account}}:{{queue}}",
                 "awsRegion":"{{_region}}"}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    private string Sns(string topic, System.Collections.IEnumerable messages) {
        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var body = JsonSerializer.Serialize(message, Wire);

            var sns =
                $$"""
                  {"Type":"Notification","MessageId":"{{topic}}-{{index}}",
                   "TopicArn":"arn:aws:sns:{{_region}}:{{_account}}:{{topic}}",
                   "Message":{{JsonSerializer.Serialize(body)}}}
                  """;

            records.Add($$"""
                {"EventVersion":"1.0","EventSource":"aws:sns",
                 "EventSubscriptionArn":"arn:aws:sns:{{_region}}:{{_account}}:{{topic}}:sub-{{index}}",
                 "Sns":{{sns}}}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    /// <summary>
    /// A DynamoDB stream batch, one MODIFY per message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message becomes the new image in DynamoDB's own wire form, so the adapter's unmarshalling
    /// runs for real rather than being handed the shape it would have produced. That is the whole
    /// difference between this delivery and the pipeline one.
    /// </para>
    /// <para>
    /// MODIFY with the same item as both images, because a test that wanted an insert or a delete is
    /// asserting on the event name, and this delivery exists to exercise the envelope rather than to
    /// model a table's history. The sequence numbers ascend, which is what a shard guarantees and
    /// what a checkpoint report is read against.
    /// </para>
    /// </remarks>
    private string DynamoDb(string table, System.Collections.IEnumerable messages) {
        var arn = $"arn:aws:dynamodb:{_region}:{_account}:table/{table}/stream/2026-01-01T00:00:00.000";

        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var image = AttributeValueWire.Item(JsonSerializer.Serialize(message, Wire));

            records.Add($$"""
                {"eventID":"{{table}}-{{index}}","eventName":"MODIFY","eventVersion":"1.1",
                 "eventSource":"aws:dynamodb","awsRegion":"{{_region}}",
                 "dynamodb":{"ApproximateCreationDateTime":1767225600,
                   "Keys":{},"NewImage":{{image}},"OldImage":{{image}},
                   "SequenceNumber":"{{Sequence(index)}}","SizeBytes":64,
                   "StreamViewType":"NEW_AND_OLD_IMAGES"},
                 "eventSourceARN":"{{arn}}"}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    /// <summary>
    /// A Kinesis batch, one record per message.
    /// </summary>
    /// <remarks>
    /// The message is serialized and base64-encoded, which is what a publisher does and what the
    /// adapter undoes - Kinesis carries an opaque blob, so the round trip through base64 is the
    /// whole of the envelope's effect on the payload. The sequence numbers ascend, which is what a
    /// shard guarantees and what a checkpoint report is read against.
    /// </remarks>
    private string Kinesis(string stream, System.Collections.IEnumerable messages) {
        var arn = $"arn:aws:kinesis:{_region}:{_account}:stream/{stream}";

        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var data = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Wire)));

            // The inner object built first, the way the SNS builder does it: a raw interpolated
            // string cannot carry two adjacent closing braces, and a Kinesis record ends with them.
            var kinesis = $$"""
                {"kinesisSchemaVersion":"1.0","partitionKey":"{{stream}}-{{index}}",
                 "sequenceNumber":"{{Sequence(index)}}","data":"{{data}}",
                 "approximateArrivalTimestamp":1767225600.0}
                """;

            records.Add($$"""
                {"eventSource":"aws:kinesis","eventVersion":"1.0",
                 "eventID":"shardId-000000000000:{{Sequence(index)}}",
                 "eventSourceARN":"{{arn}}","awsRegion":"{{_region}}",
                 "kinesis":{{kinesis}}}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    /// <summary>
    /// An S3 batch, one ObjectCreated:Put per message.
    /// </summary>
    /// <remarks>
    /// <b>The message is the notification, not the object.</b> S3 sends metadata and nothing else,
    /// so a test writing <c>blobs.Uploads(new Upload { Key = "a.txt", Size = 12 })</c> is describing
    /// what S3 would say about an object rather than the object itself - which is what a blob
    /// handler binds. The key is form-encoded on the way in, because that is what S3 does and
    /// undoing it is the adapter's job.
    /// </remarks>
    private string S3(string bucket, System.Collections.IEnumerable messages) {
        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var written = JsonSerializer.Serialize(message, Wire);

            using var document = JsonDocument.Parse(written);

            var key = document.RootElement.TryGetProperty("key", out var k) &&
                      k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? ""
                : $"object-{index}";

            var size = document.RootElement.TryGetProperty("size", out var z) &&
                       z.ValueKind == JsonValueKind.Number
                ? z.GetRawText()
                : "0";

            // Built in two pieces, because a raw interpolated string cannot carry two adjacent
            // closing braces and an S3 record nests three objects deep.
            var obj = $$"""
                {"key":"{{Uri.EscapeDataString(key).Replace("%20", "+")}}","size":{{size}},
                 "eTag":"{{index:D32}}","sequencer":"{{Sequence(index)}}"}
                """;

            var s3 = $$"""
                {"s3SchemaVersion":"1.0","configurationId":"{{bucket}}",
                 "bucket":{"name":"{{bucket}}","arn":"arn:aws:s3:::{{bucket}}"},
                 "object":{{obj}}}
                """;

            records.Add($$"""
                {"eventVersion":"2.1","eventSource":"aws:s3","awsRegion":"{{_region}}",
                 "eventTime":"2026-01-01T00:00:00.000Z","eventName":"ObjectCreated:Put",
                 "s3":{{s3}}}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    /// <summary>
    /// Ascending, and wide enough to look like one. A real sequence number is a 28-digit decimal
    /// string, and a test that asserted on the shape of the one it was reported would otherwise be
    /// asserting on an integer.
    /// </summary>
    private static string Sequence(int index) => "44215845000000000174504390" + index.ToString("D2");

    private string Scheduled(string rule) => $$"""
        {"version":"0","id":"{{rule}}-fired","detail-type":"Scheduled Event","source":"aws.events",
         "account":"{{_account}}","time":"2026-01-01T00:00:00Z","region":"{{_region}}",
         "resources":["arn:aws:events:{{_region}}:{{_account}}:rule/{{rule}}"],
         "detail":{} }
        """;

    /// <summary>Enough context to invoke, with a deadline the host turns into a token.</summary>
    private sealed class TestContext : ILambdaContext {
        public TestContext(string name) {
            FunctionName = name;
        }

        public string FunctionName { get; }

        public string AwsRequestId => Guid.NewGuid().ToString();
        public IClientContext ClientContext => null!;
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:" + FunctionName;
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/" + FunctionName;
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
