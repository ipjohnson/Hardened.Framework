using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Xunit;

namespace Hardened.Web.Testing.Tests;

public class TestWebResponseTests {

    private record Payload(string Name, int Value);

    private static Stream JsonStream(object value) =>
        new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static Stream Compressed(object value, string encoding) {
        var raw = JsonSerializer.SerializeToUtf8Bytes(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var output = new MemoryStream();
        Stream compressor = encoding == KnownEncoding.GZip
            ? new GZipStream(output, CompressionMode.Compress, true)
            : new BrotliStream(output, CompressionMode.Compress, true);

        using (compressor) {
            compressor.Write(raw, 0, raw.Length);
        }

        output.Position = 0;
        return output;
    }

    [Fact]
    public void StatusCodeDefaultsTo200WhenUnset() {
        var response = new TestWebResponse(new FakeExecutionResponse(status: null));

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public void StatusCodeReflectsUnderlyingResponse() {
        var response = new TestWebResponse(new FakeExecutionResponse(503));

        Assert.Equal(503, response.StatusCode);
    }

    [Fact]
    public void HeadersAndBodyPassThroughToUnderlyingResponse() {
        var body = new MemoryStream();
        var underlying = new FakeExecutionResponse(200, body);
        underlying.Headers["X-Custom"] = "value";

        var response = new TestWebResponse(underlying);

        Assert.Same(body, response.Body);
        Assert.Equal("value", response.Headers["X-Custom"]);
    }

    [Fact]
    public void AssertPropertyIsCachedBetweenCalls() {
        var response = new TestWebResponse(new FakeExecutionResponse(200));

        Assert.Same(response.Assert, response.Assert);
    }

    [Fact]
    public void DeserializeReadsPlainJsonBody() {
        var response = new TestWebResponse(
            new FakeExecutionResponse(200, JsonStream(new Payload("hello", 42))));

        var result = response.Deserialize<Payload>();

        Assert.Equal("hello", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void DeserializeDecompressesGZipBody() {
        var underlying = new FakeExecutionResponse(200,
            Compressed(new Payload("gzipped", 1), KnownEncoding.GZip));
        underlying.Headers[KnownHeaders.ContentEncoding] = KnownEncoding.GZipStringValues;

        var result = new TestWebResponse(underlying).Deserialize<Payload>();

        Assert.Equal("gzipped", result.Name);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void DeserializeDecompressesBrotliBody() {
        var underlying = new FakeExecutionResponse(200,
            Compressed(new Payload("brotli", 2), KnownEncoding.Br));
        underlying.Headers[KnownHeaders.ContentEncoding] = KnownEncoding.BrStringValues;

        var result = new TestWebResponse(underlying).Deserialize<Payload>();

        Assert.Equal("brotli", result.Name);
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void DeserializeThrowsForUnsupportedContentEncoding() {
        var underlying = new FakeExecutionResponse(200, JsonStream(new Payload("x", 0)));
        underlying.Headers[KnownHeaders.ContentEncoding] = "deflate";

        var response = new TestWebResponse(underlying);

        var exception = Assert.Throws<BadContentEncodingException>(() => response.Deserialize<Payload>());
        Assert.Contains("deflate", exception.Message);
    }

    [Fact]
    public async Task DeserializeAsyncEnumerableReadsNdJsonLines() {
        var ndjson =
            """
            {"name":"first","value":1}
            {"name":"second","value":2}
            {"name":"third","value":3}
            """;

        var response = new TestWebResponse(
            new FakeExecutionResponse(200, new MemoryStream(Encoding.UTF8.GetBytes(ndjson))));

        var results = new List<Payload>();
        await foreach (var item in response.DeserializeAsyncEnumerable<Payload>()) {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
        Assert.Equal("first", results[0].Name);
        Assert.Equal(2, results[1].Value);
        Assert.Equal("third", results[2].Name);
    }

    [Fact]
    public async Task DeserializeAsyncEnumerableSkipsBlankLines() {
        var ndjson = "{\"name\":\"first\",\"value\":1}\n\n   \n{\"name\":\"second\",\"value\":2}\n";

        var response = new TestWebResponse(
            new FakeExecutionResponse(200, new MemoryStream(Encoding.UTF8.GetBytes(ndjson))));

        var results = new List<Payload>();
        await foreach (var item in response.DeserializeAsyncEnumerable<Payload>()) {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("second", results[1].Name);
    }

    /// <summary>
    /// DeserializeAsyncEnumerable rewinds the body before reading, so a caller that has
    /// already consumed the stream still gets the full sequence.
    /// </summary>
    [Fact]
    public async Task DeserializeAsyncEnumerableRewindsBody() {
        var body = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"only\",\"value\":9}"));
        body.Position = body.Length;

        var response = new TestWebResponse(new FakeExecutionResponse(200, body));

        var results = new List<Payload>();
        await foreach (var item in response.DeserializeAsyncEnumerable<Payload>()) {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.Equal("only", results[0].Name);
    }
}
