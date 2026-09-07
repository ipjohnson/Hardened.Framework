using System.Text;
using System.Text.Json;
using Hardened.Aws.Lambda.Kinesis;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// The Kinesis adapter: the route it derives, the bytes a handler binds, and the report a failure
/// produces.
/// </summary>
public class KinesisAdapterTests {
    private static KinesisRequest Request(bool reportsItemFailures = false) {
        using var payload = Infrastructure.Payloads.Payload(Infrastructure.Payloads.KinesisJson);

        return (KinesisRequest)new KinesisAdapter(reportsItemFailures)
            .CreateRequest(payload, new TestLambdaContext());
    }

    private static string Body(IExecutionRequest request) {
        request.Body!.Position = 0;

        // leaveOpen, because a StreamReader closes what it wraps and a test may read a
        // request twice.
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);

        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData("arn:aws:kinesis:us-east-1:123456789012:stream/clickstream", "clickstream")]
    [InlineData("not-an-arn", "not-an-arn")]
    [InlineData(null, "")]
    public void TheStreamNameComesFromTheArn(string? arn, string expected) =>
        Assert.Equal(expected, KinesisAdapter.StreamName(arn));

    [Fact]
    public void TheBatchRoutesOnTheStreamAndTheStreamScheme() {
        var request = Request();

        Assert.Equal("STREAM", request.Method);
        Assert.Equal("/orders", request.Path);
    }

    /// <summary>
    /// A shard is a log, so the run stops at the first failure rather than attempting records that
    /// are being redelivered anyway.
    /// </summary>
    [Fact]
    public void ADeliveryReportsByCheckpoint() =>
        Assert.Equal(BatchFailureMode.Checkpoint, Request().FailureMode);

    /// <summary>
    /// The handler binds the publisher's own bytes, decoded and nothing else.
    /// </summary>
    /// <remarks>
    /// The whole difference from a change feed. Kinesis wraps a blob it knows nothing about, so
    /// there is no image to flatten and no serializer context to carry - which is also why this
    /// adapter takes no Amazon event package.
    /// </remarks>
    [Fact]
    public void ARecordBindsItsDecodedData() =>
        Assert.Equal("""{"id":"a-1","quantity":7}""", Body(Request().ForItem(0)));

    /// <summary>
    /// Each fork carries its own record, in the order the shard delivered them.
    /// </summary>
    [Fact]
    public void EachRecordKeepsItsOwnDataAndItsOrder() {
        var request = Request();

        Assert.Equal(2, request.Count);
        Assert.Equal("""{"id":"a-1","quantity":7}""", Body(request.ForItem(0)));
        Assert.Equal("""{"id":"a-2","quantity":9}""", Body(request.ForItem(1)));
    }

    /// <summary>
    /// The envelope becomes headers, so a handler can reach the shard position without the batch.
    /// </summary>
    /// <remarks>
    /// The arrival time is carried as the text the envelope held. It is epoch seconds with a
    /// fraction, which is exactly the shape that throws when a model types the field as a DateTime.
    /// </remarks>
    [Fact]
    public void ARecordCarriesItsEnvelopeAsHeaders() {
        var headers = Request().ForItem(0).Headers;

        Assert.Equal("p1", headers[KinesisRequest.PartitionKeyHeader].ToString());
        Assert.Equal("49590", headers[KinesisRequest.SequenceNumberHeader].ToString());
        Assert.Equal("shardId-000000000000:49590", headers[KinesisRequest.EventIdHeader].ToString());
        Assert.Equal("1545084650.987", headers[KinesisRequest.ArrivalTimeHeader].ToString());
        Assert.Equal(
            "arn:aws:kinesis:us-east-1:123456789012:stream/orders",
            headers[KinesisRequest.StreamArnHeader].ToString());
    }

    /// <summary>
    /// The report names the sequence number, never the event id.
    /// </summary>
    /// <remarks>
    /// The event id carries a shard prefix and is not a position Lambda can resolve a checkpoint
    /// from. Hardened.Amz shipped that mistake on the DynamoDB side and silently re-drove the whole
    /// batch for it.
    /// </remarks>
    [Fact]
    public async Task AFailureIsReportedBySequenceNumber() {
        var adapter = new KinesisAdapter(reportsItemFailures: true);
        var request = Request(reportsItemFailures: true);

        request.RecordFailure(1, new InvalidOperationException("no"));

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), request), output);

        output.Position = 0;

        var failures = JsonDocument.Parse(output).RootElement.GetProperty("batchItemFailures");

        Assert.Equal("49591", failures[0].GetProperty("itemIdentifier").GetString());
    }

    [Fact]
    public async Task ABatchWithNoFailureReportsAnEmptyList() {
        var adapter = new KinesisAdapter();

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), Request()), output);

        output.Position = 0;

        Assert.Empty(JsonDocument.Parse(output).RootElement.GetProperty("batchItemFailures")
            .EnumerateArray());
    }

    /// <summary>
    /// A record whose data is not base64 gives an empty body rather than failing the batch.
    /// </summary>
    /// <remarks>
    /// Kinesis will not produce one. If something does, the handler rejecting it is a reportable
    /// item failure, where a throw inside the adapter would take every other record with it.
    /// </remarks>
    [Fact]
    public void AnUndecodableRecordGivesAnEmptyBody() {
        var json = Infrastructure.Payloads.KinesisJson
            .Replace("eyJpZCI6ImEtMSIsInF1YW50aXR5Ijo3fQ==", "not-base64!!");

        using var payload = Infrastructure.Payloads.Payload(json);

        var request = (KinesisRequest)new KinesisAdapter()
            .CreateRequest(payload, new TestLambdaContext());

        Assert.Equal("", Body(request.ForItem(0)));
        Assert.Equal(2, request.Count);
    }
}
