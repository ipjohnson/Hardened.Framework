using System.Text;
using System.Text.Json;
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// The DynamoDB Streams adapter: the route it derives, the item a handler binds, and the report a
/// failure produces.
/// </summary>
public class DynamoDbAdapterTests {
    private static DynamoDbRequest Request(bool reportsItemFailures = false) {
        using var payload = Infrastructure.Payloads.Payload(Infrastructure.Payloads.DynamoDbJson);

        return (DynamoDbRequest)new DynamoDbAdapter(reportsItemFailures)
            .CreateRequest(payload, new TestLambdaContext());
    }

    private static JsonElement Body(IExecutionRequest request) {
        request.Body!.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);

        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }

    /// <summary>
    /// The table's name comes out of the middle of the stream ARN, not off the end.
    /// </summary>
    /// <remarks>
    /// The end is the timestamp the stream was enabled at, so an adapter that took the last segment
    /// the way the SQS one does would route every table to a different path each time its stream
    /// was turned off and on.
    /// </remarks>
    [Theory]
    [InlineData("arn:aws:dynamodb:us-east-1:1:table/orders/stream/2026-01-01T00:00:00.000", "orders")]
    [InlineData("arn:aws:dynamodb:eu-west-2:1:table/orders", "orders")]
    [InlineData("not-an-arn", "not-an-arn")]
    [InlineData(null, "")]
    public void TheTableNameComesFromTheStreamArn(string? arn, string expected) =>
        Assert.Equal(expected, DynamoDbAdapter.TableName(arn));

    [Fact]
    public void TheBatchRoutesOnTheTableAndTheChangeScheme() {
        var request = Request();

        Assert.Equal("CHANGE", request.Method);
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
    /// The handler binds the row, with DynamoDB's type wrappers stripped off.
    /// </summary>
    /// <remarks>
    /// The point of the adapter. Without this a handler declares
    /// <c>Dictionary&lt;string, AttributeValue&gt;</c> and reads <c>item["total"].N</c> as a string.
    /// </remarks>
    [Fact]
    public void AChangeBindsTheNewImageAsPlainJson() {
        var body = Body(Request().ForItem(0));

        Assert.Equal("order-1", body.GetProperty("id").GetString());
        Assert.Equal(42.5m, body.GetProperty("total").GetDecimal());
        Assert.True(body.GetProperty("paid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancelled").ValueKind);
        Assert.Equal(["rush", "gift"], body.GetProperty("tags").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal([1, 2], body.GetProperty("sizes").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal("Leeds", body.GetProperty("shipping").GetProperty("city").GetString());
    }

    /// <summary>
    /// A list keeps its members' own types rather than becoming strings.
    /// </summary>
    [Fact]
    public void AListBindsItsMembersByTheirOwnTypes() {
        var lines = Body(Request().ForItem(0)).GetProperty("lines").EnumerateArray().ToArray();

        Assert.Equal("a", lines[0].GetString());
        Assert.Equal(7, lines[1].GetInt32());
    }

    /// <summary>
    /// A number keeps the text DynamoDB stored, so a value wider than a double survives.
    /// </summary>
    /// <remarks>
    /// DynamoDB stores every number as a decimal string with 38 digits of precision. Writing one
    /// back through a double would round it here, before the handler's own type ever saw it.
    /// </remarks>
    [Fact]
    public void ANumberKeepsThePrecisionDynamoDbStored() {
        var json = Infrastructure.Payloads.DynamoDbJson.Replace(
            "\"total\":{\"N\":\"42.5\"}", "\"total\":{\"N\":\"123456789012345678901234567890.5\"}");

        using var payload = Infrastructure.Payloads.Payload(json);

        var request = new DynamoDbAdapter().CreateRequest(payload, new TestLambdaContext());

        Assert.Equal(
            "123456789012345678901234567890.5",
            Body(((DynamoDbRequest)request).ForItem(0)).GetProperty("total").GetRawText());
    }

    /// <summary>
    /// The envelope becomes headers, so a handler can tell an insert from an update without
    /// binding the record.
    /// </summary>
    [Fact]
    public void TheChangeCarriesItsEnvelopeAsHeaders() {
        var headers = Request().ForItem(0).Headers;

        Assert.Equal("MODIFY", headers[DynamoDbRequest.EventNameHeader].ToString());
        Assert.Equal("4421584500000000017450439091",
            headers[DynamoDbRequest.SequenceNumberHeader].ToString());
        Assert.Equal("NEW_AND_OLD_IMAGES", headers[DynamoDbRequest.StreamViewTypeHeader].ToString());
    }

    /// <summary>
    /// Both images stay reachable on the fork, which is what <c>[NewImage]</c> and
    /// <c>[OldImage]</c> read.
    /// </summary>
    [Fact]
    public void AChangeKeepsBothImagesOnTheRequest() {
        var change = Assert.IsType<DynamoDbChange>(Request().ForItem(0));

        Assert.Equal("42.5", change.NewImage!["total"].N);
        Assert.Equal("10", change.OldImage!["total"].N);
    }

    /// <summary>
    /// The report names the sequence number, never the event id.
    /// </summary>
    /// <remarks>
    /// Hardened.Amz reported <c>EventID</c> until 2026-08-15. It identifies a record but is not a
    /// position in the shard, so Lambda could resolve no checkpoint from it and silently re-drove
    /// the whole batch - which is the outcome partial batch reporting exists to avoid.
    /// </remarks>
    [Fact]
    public async Task AFailureIsReportedBySequenceNumber() {
        var adapter = new DynamoDbAdapter(reportsItemFailures: true);
        var request = Request(reportsItemFailures: true);

        request.RecordFailure(0, new InvalidOperationException("no"));

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), request), output);

        output.Position = 0;

        var failures = JsonDocument.Parse(output).RootElement.GetProperty("batchItemFailures");

        Assert.Equal(
            "4421584500000000017450439091",
            failures[0].GetProperty("itemIdentifier").GetString());
    }

    [Fact]
    public async Task ABatchWithNoFailureReportsAnEmptyList() {
        var adapter = new DynamoDbAdapter();

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), Request()), output);

        output.Position = 0;

        Assert.Empty(JsonDocument.Parse(output).RootElement.GetProperty("batchItemFailures")
            .EnumerateArray());
    }
}
