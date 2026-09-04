using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Compression;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Compression;

/// <summary>
/// Compressed request bodies. A client that sets <c>Content-Encoding</c> is telling the server the
/// bytes are not the representation yet, and a reader that ignores the header reads gzip's magic
/// number as the first character of a document.
///
/// <para>
/// The table here is the one the two JSON deserializers were held to while they did the decoding
/// themselves, moved to the filter unchanged, plus what the filter adds: the cap, the 415 with its
/// header, and the header being gone by the time anything downstream reads the body.
/// </para>
/// </summary>
public class RequestDecompressionFilterTests {

    private const string Json = """{"name":"encoded","value":7}""";

    private static RequestDecompressionFilter Filter(long? cap = null) =>
        new(new CompressionConfiguration {
            MaxDecompressedRequestBytes = cap ?? CompressionConfiguration.DefaultMaxDecompressedRequestBytes
        });

    private static IExecutionContext Context(byte[] body, StringValues contentEncoding = default) {
        var context = Pipeline.Context(method: "POST", body: body);

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");
        context.Request.Headers[KnownHeaders.ContentLength] = body.Length.ToString();

        if (!StringValues.IsNullOrEmpty(contentEncoding)) {
            context.Request.Headers[KnownHeaders.ContentEncoding] = contentEncoding;
        }

        return context;
    }

    private static byte[] GZipped(string content) => Encode(content, output => new GZipStream(output, CompressionLevel.Fastest, true));

    private static byte[] Brotlied(string content) => Encode(content, output => new BrotliStream(output, CompressionLevel.Fastest, true));

    private static byte[] Encode(string content, Func<Stream, Stream> encoder) {
        var output = new MemoryStream();

        using (var encoding = encoder(output)) {
            var bytes = Encoding.UTF8.GetBytes(content);

            encoding.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Runs the filter over a stage that reads the whole body as text and records what the
    /// request looked like from inside, which is where the bind would read it.
    /// </summary>
    private static async Task<(string Body, Dictionary<string, StringValues> Headers)> ReadInside(
        IExecutionContext context, RequestDecompressionFilter? filter = null) {
        string body = "";
        Dictionary<string, StringValues> headers = new();

        await Pipeline.Chain(context, filter ?? Filter(), new Pipeline.Inline(async chain => {
            using var reader = new StreamReader(chain.Context.Request.Body, Encoding.UTF8, leaveOpen: true);

            body = await reader.ReadToEndAsync();
            headers = new Dictionary<string, StringValues>(chain.Context.Request.Headers);
        })).Next();

        return (body, headers);
    }

    [Fact]
    public async Task AnUncompressedBodyIsLeftAlone() {
        var (body, headers) = await ReadInside(Context(Encoding.UTF8.GetBytes(Json)));

        Assert.Equal(Json, body);
        Assert.True(headers.ContainsKey(KnownHeaders.ContentLength));
    }

    [Fact]
    public async Task AGzippedBodyIsDecodedBeforeAnythingReadsIt() {
        var (body, _) = await ReadInside(Context(GZipped(Json), KnownEncoding.GZip));

        Assert.Equal(Json, body);
    }

    [Fact]
    public async Task ABrotliBodyIsDecodedBeforeAnythingReadsIt() {
        var (body, _) = await ReadInside(Context(Brotlied(Json), KnownEncoding.Br));

        Assert.Equal(Json, body);
    }

    /// <summary>
    /// <c>Content-Encoding</c> may carry several values; the coding is recognised beside
    /// <c>identity</c>, which is the absence of one, whether the two arrive as two values or one.
    /// </summary>
    [Theory]
    [InlineData("identity", KnownEncoding.GZip)]
    [InlineData("identity, gzip")]
    public async Task ACodingIsRecognisedBesideIdentity(params string[] values) {
        var (body, _) = await ReadInside(Context(GZipped(Json), new StringValues(values)));

        Assert.Equal(Json, body);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("")]
    public async Task IdentityMeansNoCoding(string value) {
        var context = Context(Encoding.UTF8.GetBytes(Json), value);

        var (body, _) = await ReadInside(context);

        Assert.Equal(Json, body);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// The header is gone by the time the bind runs, so nothing downstream decodes a second time,
    /// and so is <c>Content-Length</c>, which measured the bytes on the wire and no longer
    /// describes the body anything will read.
    /// </summary>
    [Fact]
    public async Task TheCodingHeadersAreRemovedForEverythingDownstream() {
        var (_, headers) = await ReadInside(Context(GZipped(Json), KnownEncoding.GZip));

        Assert.False(headers.ContainsKey(KnownHeaders.ContentEncoding));
        Assert.False(headers.ContainsKey(KnownHeaders.ContentLength));
    }

    /// <summary>
    /// The decoder is put back after the chain, so a retry or a logger reading the request
    /// afterwards sees the stream the transport supplied rather than a disposed decoder.
    /// </summary>
    [Fact]
    public async Task TheTransportBodyIsRestoredAfterTheChain() {
        var context = Context(GZipped(Json), KnownEncoding.GZip);
        var transport = context.Request.Body;

        await ReadInside(context);

        Assert.Same(transport, context.Request.Body);
    }

    /// <summary>
    /// A coding the filter cannot decode is the client's mistake, and RFC 9110 says a 415 carrying
    /// <c>Accept-Encoding</c> rather than the 400 the deserializers used to raise. Recorded rather
    /// than thrown, because everything ahead of serialization refuses that way so the filter that
    /// writes the response is still reached.
    /// </summary>
    [Theory]
    [InlineData("deflate")]
    [InlineData("compress")]
    [InlineData("zstd")]
    public async Task AnUnsupportedCodingIsA415NamingWhatWasSent(string encoding) {
        var context = Context(Encoding.UTF8.GetBytes(Json), encoding);
        var reached = false;

        await Pipeline.Chain(context, Filter(), new Pipeline.Inline(_ => {
            reached = true;

            return Task.CompletedTask;
        })).Next();

        var exception = Assert.IsType<BadContentEncodingException>(context.Response.ExceptionValue);

        Assert.True(reached);
        Assert.Equal(415, exception.StatusCode);
        Assert.Contains(encoding, exception.Message);
    }

    /// <summary>
    /// A 415 for a content coding is not well-formed without saying what is supported.
    /// </summary>
    [Fact]
    public void The415CarriesTheCodingsTheFilterDecodes() {
        var headers = new Dictionary<string, StringValues>();

        new BadContentEncodingException("deflate").ApplyHeaders(headers);

        Assert.Equal("gzip, br", headers[KnownHeaders.AcceptEncoding].ToString());
    }

    /// <summary>
    /// <see cref="BadContentEncodingException"/> is a 415 by type. It was a 400 by deriving from
    /// <see cref="BadRequestException"/>, and reached 400 before that only by having "Bad" in its
    /// name.
    /// </summary>
    [Fact]
    public void AnUnsupportedCodingIsAStatusByType() {
        var exception = new BadContentEncodingException("deflate");

        Assert.IsAssignableFrom<IStatusCodeException>(exception);
        Assert.Equal(415, exception.StatusCode);
    }

    /// <summary>
    /// Two codings is a body compressed twice, which the filter does not unwrap, and the refusal
    /// names the whole value so the caller can see which part was the problem.
    /// </summary>
    [Fact]
    public async Task ABodyCodedTwiceIsRefusedUnderTheWholeValue() {
        var context = Context(GZipped(Json), "gzip, br");

        await Pipeline.Chain(context, Filter(), new Pipeline.Inline(_ => Task.CompletedTask)).Next();

        var exception = Assert.IsType<BadContentEncodingException>(context.Response.ExceptionValue);

        Assert.Contains("gzip, br", exception.Message);
    }

    /// <summary>
    /// A body that claims gzip and is not gzip fails as a malformed stream rather than being read
    /// as text.
    /// </summary>
    [Fact]
    public async Task ABodyThatLiesAboutItsCodingFailsToRead() {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await ReadInside(Context(Encoding.UTF8.GetBytes(Json), KnownEncoding.GZip)));
    }

    /// <summary>
    /// The cap is on the decoded size, because the encoded size is the one number a hostile client
    /// controls: a gzip member of a few hundred bytes decodes to gigabytes. Past it the read
    /// throws a 413, which the bind's caller records like any other failure to read the body.
    /// </summary>
    [Fact]
    public async Task ABodyDecodingPastTheCapIsA413() {
        var large = new string('x', 1000);

        var exception = await Assert.ThrowsAsync<DecompressedBodyTooLargeException>(async () =>
            await ReadInside(Context(GZipped(large), KnownEncoding.GZip), Filter(cap: 100)));

        Assert.Equal(413, exception.StatusCode);
        Assert.Equal(100, exception.Limit);
        Assert.Contains("100", exception.Message);
    }

    [Fact]
    public async Task ABodyDecodingToExactlyTheCapIsRead() {
        var exact = new string('x', 100);

        var (body, _) = await ReadInside(Context(GZipped(exact), KnownEncoding.GZip), Filter(cap: 100));

        Assert.Equal(exact, body);
    }

    /// <summary>
    /// The filter runs ahead of the cache, so <c>ByPayload</c> hashes identity bytes and a gzip
    /// body shares an entry with its plain twin rather than filling a second one.
    /// </summary>
    [Fact]
    public async Task AGzipBodyAndItsPlainTwinShareAByPayloadKey() {
        var plain = await KeyInside(Context(Encoding.UTF8.GetBytes(Json)));
        var gzipped = await KeyInside(Context(GZipped(Json), KnownEncoding.GZip));

        Assert.NotNull(plain);
        Assert.Equal(plain, gzipped);
    }

    private static async Task<string?> KeyInside(IExecutionContext context) {
        string? key = null;

        await Pipeline.Chain(context, Filter(), new Pipeline.Inline(async chain => {
            key = await ByPayload.Create([]).Key(chain.Context);
        })).Next();

        return key;
    }
}
