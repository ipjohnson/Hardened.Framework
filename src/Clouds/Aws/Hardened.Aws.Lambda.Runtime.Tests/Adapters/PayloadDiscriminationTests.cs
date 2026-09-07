using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Xunit;
using Hardened.Aws.Lambda.ApiGateway;
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Aws.Lambda.EventBridge;
using Hardened.Aws.Lambda.Kinesis;
using Hardened.Aws.Lambda.S3;
using Hardened.Aws.Lambda.Sns;
using Hardened.Aws.Lambda.Sqs;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// Every adapter against every payload, which is the assertion the family split rests on.
/// </summary>
/// <remarks>
/// <para>
/// Testing each adapter's <c>Handles</c> in its own file proves each one recognises its own event.
/// It does not prove the set is <em>mutually exclusive</em>, and that is the property that matters:
/// if two adapters can claim one payload, which one wins depends on registration order, and the
/// design's claim that order does not matter is false.
/// </para>
/// <para>
/// SQS, SNS, DynamoDB Streams and Kinesis all arrive as a <c>Records</c> array, so this is where an
/// adapter matching on the array rather than on the event source value is caught.
/// </para>
/// </remarks>
public class PayloadDiscriminationTests {
    private static readonly (string Name, IPayloadAdapter Adapter)[] Adapters = [
        ("sqs", new SqsAdapter()),
        ("sns", new SnsAdapter()),
        ("dynamodb", new DynamoDbAdapter()),
        ("kinesis", new KinesisAdapter()),
        ("s3", new S3Adapter()),
        ("eventbridge", new EventBridgeAdapter()),
        ("apigateway", new ApiGatewayAdapter())
    ];

    public static TheoryData<string, string> Payloads() => new() {
        { "sqs", Infrastructure.Payloads.SqsJson },
        { "sns", Infrastructure.Payloads.SnsJson },
        { "dynamodb", Infrastructure.Payloads.DynamoDbJson },
        { "kinesis", Infrastructure.Payloads.KinesisJson },
        { "s3", Infrastructure.Payloads.S3Json },
        { "eventbridge", Infrastructure.Payloads.EventBridgeJson },
        { "eventbridge", Infrastructure.Payloads.ScheduledJson },
        { "apigateway", Infrastructure.Payloads.ApiGatewayJson }
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void ExactlyOneAdapterClaimsEachPayload(string expected, string json) {
        using var payload = Infrastructure.Payloads.Payload(json);

        var claimed = Adapters.Where(a => a.Adapter.Handles(payload.Json)).Select(a => a.Name).ToArray();

        Assert.Equal([expected], claimed);
    }

    /// <summary>
    /// Sources no adapter has been written for yet. Every one of them is a <c>Records</c> array or
    /// close to it, and none may be claimed by an adapter that was not written for it - a Kinesis
    /// batch handled as SQS would report every record as successfully processed.
    /// </summary>
    /// <remarks>
    /// DynamoDB Streams, then Kinesis, then S3 left this list when their adapters arrived, and moved into
    /// <see cref="Payloads"/> above where each now has to be claimed by exactly one thing. That is
    /// the shape of adding an adapter: a row moves from here to there. Firehose is what is left,
    /// and it is not a Records array at all - it is a synchronous transform, which is why it needs
    /// its own trigger rather than an adapter under [Stream].
    /// </remarks>
    [Theory]
    [InlineData("firehose")]
    public void NothingClaimsASourceWithNoAdapter(string source) {
        var json = source switch {
            _ => Infrastructure.Payloads.FirehoseJson
        };

        using var payload = Infrastructure.Payloads.Payload(json);

        Assert.Empty(Adapters.Where(a => a.Adapter.Handles(payload.Json)).Select(a => a.Name));
    }

    /// <summary>
    /// A caller's own payload, which is what a direct invocation carries. Nothing may claim it,
    /// which is what sends it to the invoke adapter - and why invoke gets a function of its own.
    /// </summary>
    [Theory]
    [InlineData("""{"orderId":"abc","quantity":2}""")]
    [InlineData("{}")]
    [InlineData("""{"Records":[]}""")]
    [InlineData("""{"Records":[{"eventSource":"acme:custom"}]}""")]
    [InlineData("""{"source":"com.acme.orders"}""")]
    [InlineData("[]")]
    [InlineData("42")]
    public void NothingClaimsAnApplicationsOwnPayload(string json) {
        using var payload = Infrastructure.Payloads.Payload(json);

        Assert.Empty(Adapters.Where(a => a.Adapter.Handles(payload.Json)).Select(a => a.Name));
    }
}
