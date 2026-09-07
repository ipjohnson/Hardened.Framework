using System.Text;
using System.Text.Json;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Aws.Lambda.S3;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// The S3 adapter: the route it derives, the notification a handler binds, and the key it decodes.
/// </summary>
public class S3AdapterTests {
    private static S3Request Request() {
        using var payload = Infrastructure.Payloads.Payload(Infrastructure.Payloads.S3Json);

        return (S3Request)new S3Adapter().CreateRequest(payload, new TestLambdaContext());
    }

    private static JsonElement Body(IExecutionRequest request) {
        request.Body!.Position = 0;

        // leaveOpen, because a StreamReader closes what it wraps and a test may read a
        // request twice.
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);

        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }

    /// <summary>
    /// The key is decoded, which is the single thing this adapter gets wrong if it does nothing.
    /// </summary>
    /// <remarks>
    /// S3 form-encodes the key in a notification, so a space arrives as a plus. A handler passing
    /// the raw value back to GetObject asks for a key that does not exist, and the failure looks
    /// like a missing object rather than a decoding bug.
    /// </remarks>
    [Theory]
    [InlineData("my+report.pdf", "my report.pdf")]
    [InlineData("folder/sub+dir/a+b.txt", "folder/sub dir/a b.txt")]
    [InlineData("caf%C3%A9.txt", "café.txt")]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("", "")]
    public void TheKeyIsDecoded(string encoded, string expected) =>
        Assert.Equal(expected, S3Adapter.DecodeKey(encoded));

    /// <summary>
    /// A key that genuinely contains a plus survives, because S3 sends it percent-encoded.
    /// </summary>
    /// <remarks>
    /// The case that makes UnescapeDataString the wrong tool and UrlDecode alone the wrong tool
    /// too: one leaves the plus that meant a space, the other would eat a plus that meant a plus.
    /// </remarks>
    [Fact]
    public void APlusInTheKeyItselfSurvives() =>
        Assert.Equal("c++notes.txt", S3Adapter.DecodeKey("c%2B%2Bnotes.txt"));

    [Fact]
    public void TheBatchRoutesOnTheBucketAndTheBlobScheme() {
        var request = Request();

        Assert.Equal("BLOB", request.Method);
        Assert.Equal("/uploads", request.Path);
    }

    /// <summary>
    /// Independent notifications, so a queue's failure shape rather than a shard's.
    /// </summary>
    [Fact]
    public void ADeliveryReportsPerItem() =>
        Assert.Equal(BatchFailureMode.PerItem, Request().FailureMode);

    /// <summary>
    /// S3 reads no response, so nothing can be reported and saying so is better than pretending.
    /// </summary>
    [Fact]
    public void AnIndividualFailureCannotBeReported() {
        var request = Request();

        Assert.False(request.ReportsItemFailures);
        Assert.Throws<NotSupportedException>(
            () => request.RecordFailure(0, new InvalidOperationException("no")));
    }

    /// <summary>
    /// The handler binds the notification, because the object is not in it.
    /// </summary>
    [Fact]
    public void ANotificationBindsAsAFlatRecord() {
        var body = Body(Request().ForItem(0));

        Assert.Equal("uploads", body.GetProperty("bucket").GetString());
        Assert.Equal("my report.pdf", body.GetProperty("key").GetString());
        Assert.Equal(1024, body.GetProperty("size").GetInt64());
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", body.GetProperty("eTag").GetString());
        Assert.Equal("ObjectCreated:Put", body.GetProperty("eventName").GetString());
    }

    /// <summary>
    /// A delete carries no size, and null is not zero.
    /// </summary>
    /// <remarks>
    /// Zero would be indistinguishable from an empty object, which is a thing S3 lets you create.
    /// </remarks>
    [Fact]
    public void ADeleteCarriesNoSize() {
        var request = Request();
        var body = Body(request.ForItem(1));

        Assert.Null(request.Records[1].Size);
        Assert.False(body.TryGetProperty("size", out _));
        Assert.Equal("ObjectRemoved:Delete", body.GetProperty("eventName").GetString());
    }

    /// <summary>
    /// The envelope becomes headers, including the sequencer a handler needs to order two
    /// notifications for one key.
    /// </summary>
    [Fact]
    public void ANotificationCarriesItsEnvelopeAsHeaders() {
        var headers = Request().ForItem(0).Headers;

        Assert.Equal("my report.pdf", headers[S3Request.KeyHeader].ToString());
        Assert.Equal("ObjectCreated:Put", headers[S3Request.EventNameHeader].ToString());
        Assert.Equal("00659A1B2C3D4E5F60", headers[S3Request.SequencerHeader].ToString());
        Assert.Equal("uploads", headers[S3Request.BucketHeader].ToString());
    }

    /// <summary>
    /// Nothing is written back, because nothing reads it.
    /// </summary>
    [Fact]
    public async Task NothingIsWrittenToTheResponse() {
        var adapter = new S3Adapter();

        var output = new MemoryStream();

        await adapter.WriteResponse(
            new ResponseOnlyContext(adapter.CreateResponse(new MemoryStream()), Request()), output);

        Assert.Equal(0, output.Length);
    }
}
