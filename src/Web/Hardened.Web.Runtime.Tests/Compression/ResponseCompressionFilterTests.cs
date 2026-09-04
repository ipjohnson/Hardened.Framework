using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Compression;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Compression;

/// <summary>
/// The response compression filter, driven with real request, response and chain objects and a
/// stage in the handler's position that writes what a serializer would.
///
/// <para>
/// Negotiation is decided at filter entry and asserted through the coding chosen; whether the
/// body is compressed at all is decided on the first write and asserted through the bytes that
/// reach the transport and the headers beside them.
/// </para>
/// </summary>
public class ResponseCompressionFilterTests {

    private const string Json = """{"name":"compress me","value":7}""";

    private const string Browser = "gzip, deflate, br, zstd";

    // ---------------------------------------------------------------- fixtures

    private static IExecutionContext Context(
        string? acceptEncoding = Browser,
        IServiceProvider? services = null,
        string path = "/pets") {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (acceptEncoding != null) {
            headers[KnownHeaders.AcceptEncoding] = acceptEncoding;
        }

        var request = new TestExecutionRequest(
            "GET", path, "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Headers = headers
        };

        var provider = services ?? new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            provider, provider, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    private static ResponseCompressionFilter Filter(
        ICompressionPredicate? predicate = null,
        CompressionType favor = CompressionType.Default,
        CompressionConfiguration? configuration = null) =>
        new(predicate, favor, configuration ?? new CompressionConfiguration());

    /// <summary>
    /// Runs the filters over a final stage standing in for the serializer.
    /// </summary>
    private static async Task Run(IExecutionContext context, Func<IExecutionChain, Task> handler, params IExecutionFilter[] filters) {
        var chain = filters.Select<IExecutionFilter, Func<IExecutionContext, IExecutionFilter>>(filter => _ => filter)
            .Append(_ => new Stage(handler))
            .ToList();

        await new ExecutionChain(chain, context).Next();
    }

    /// <summary>
    /// A handler that sets what a serializer would and writes <paramref name="body"/>.
    /// </summary>
    private static Func<IExecutionChain, Task> Writes(
        string body, string contentType = "application/json", int? status = null, object? value = null) =>
        async chain => {
            var response = chain.Context.Response;

            response.Status = status;
            response.ContentType = contentType;
            response.ResponseValue = value;

            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(body));
        };

    private static byte[] Transport(IExecutionContext context) =>
        ((MemoryStream)context.Response.Body).ToArray();

    private static string Encoding_(IExecutionContext context) =>
        context.Response.Headers.TryGetValue(KnownHeaders.ContentEncoding, out var value) ? value.ToString() : "";

    private static bool LooksGzip(byte[] bytes) => bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;

    private static string Decode(byte[] bytes, string coding) {
        using var input = new MemoryStream(bytes);
        using Stream decoder = coding == KnownEncoding.Br
            ? new BrotliStream(input, CompressionMode.Decompress)
            : new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(decoder, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private sealed class Stage : IExecutionFilter {
        private readonly Func<IExecutionChain, Task> _body;

        public Stage(Func<IExecutionChain, Task> body) {
            _body = body;
        }

        public Task Execute(IExecutionChain chain) => _body(chain);
    }

    /// <summary>Answers what the test says, and remembers what it was shown.</summary>
    private sealed class Predicate : ICompressionPredicate {
        private readonly bool _answer;

        public Predicate(bool answer) {
            _answer = answer;
        }

        public List<object> Seen { get; } = [];

        public static ICompressionPredicate Create(object[] args) => new Predicate(true);

        public bool ShouldCompress(object value, IExecutionContext context) {
            Seen.Add(value);

            return _answer;
        }
    }

    // ---------------------------------------------------------------- negotiation

    [Fact]
    public async Task ABrowserHeaderGetsGzip() {
        var context = Context(Browser);

        await Run(context, Writes(Json), Filter());

        Assert.Equal("gzip", Encoding_(context));
        Assert.True(LooksGzip(Transport(context)));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.GZip));
    }

    [Fact]
    public async Task BrotliIsChosenWhenFavouredAndAccepted() {
        var context = Context(Browser);

        await Run(context, Writes(Json), Filter(favor: CompressionType.Br));

        Assert.Equal("br", Encoding_(context));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.Br));
    }

    [Fact]
    public async Task GzipIsChosenWhenFavouredAheadOfAConfiguredBrotli() {
        var context = Context(Browser);
        var configuration = new CompressionConfiguration { Encodings = [KnownEncoding.Br, KnownEncoding.GZip] };

        await Run(context, Writes(Json), Filter(favor: CompressionType.GZip, configuration: configuration));

        Assert.Equal("gzip", Encoding_(context));
    }

    [Fact]
    public async Task AFavouredCodingTheClientDoesNotAcceptFallsBackToTheOrder() {
        var context = Context("gzip");

        await Run(context, Writes(Json), Filter(favor: CompressionType.Br));

        Assert.Equal("gzip", Encoding_(context));
    }

    /// <summary>
    /// An operation reorders the codings the application offers; it cannot re-enable one the
    /// application turned off.
    /// </summary>
    [Fact]
    public async Task AFavouredCodingTheConfigurationDoesNotOfferIsNotUsed() {
        var context = Context(Browser);
        var configuration = new CompressionConfiguration { Encodings = [KnownEncoding.GZip] };

        await Run(context, Writes(Json), Filter(favor: CompressionType.Br, configuration: configuration));

        Assert.Equal("gzip", Encoding_(context));
    }

    [Fact]
    public async Task TheConfiguredOrderDecidesWhenNothingIsFavoured() {
        var context = Context(Browser);
        var configuration = new CompressionConfiguration { Encodings = [KnownEncoding.Br, KnownEncoding.GZip] };

        await Run(context, Writes(Json), Filter(configuration: configuration));

        Assert.Equal("br", Encoding_(context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("identity")]
    [InlineData("deflate, zstd")]
    public async Task AClientAcceptingNothingOfferedIsServedIdentity(string? acceptEncoding) {
        var context = Context(acceptEncoding);

        await Run(context, Writes(Json), Filter());

        Assert.Equal("", Encoding_(context));
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(context)));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Vary));
    }

    /// <summary>
    /// API Gateway delivers header names lowercased.
    /// </summary>
    [Fact]
    public async Task TheAcceptHeaderIsReadWhateverItsCase() {
        var headers = new Dictionary<string, StringValues> { ["accept-encoding"] = "gzip" };
        var request = new TestExecutionRequest("GET", "/pets", "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) { Headers = headers };
        var provider = new ServiceCollection().BuildServiceProvider();
        var context = new TestExecutionContext(provider, provider, Substitute.For<IKnownServices>(),
            request, new TestExecutionResponse(new MemoryStream()), CancellationToken.None);

        await Run(context, Writes(Json), Filter());

        Assert.Equal("gzip", Encoding_(context));
    }

    // ---------------------------------------------------------------- headers

    [Fact]
    public async Task ACompressedResponseVariesOnAcceptEncoding() {
        var context = Context();

        await Run(context, Writes(Json), Filter());

        Assert.Equal("Accept-Encoding", context.Response.Headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>
    /// The CORS filter runs first and says <c>Origin</c>. Assigning here would erase that, which
    /// is the one thing a shared cache must not be allowed to forget.
    /// </summary>
    [Fact]
    public async Task VaryIsMergedWithWhatWasAlreadyThere() {
        var context = Context();

        context.Response.Headers[KnownHeaders.Vary] = KnownHeaders.Origin;

        await Run(context, Writes(Json), Filter());

        Assert.Equal("Origin, Accept-Encoding", context.Response.Headers[KnownHeaders.Vary].ToString());
    }

    [Fact]
    public async Task AnAnnouncedContentLengthIsDropped() {
        var context = Context();

        await Run(context, async chain => {
            chain.Context.Response.Headers[KnownHeaders.ContentLength] = Json.Length.ToString();

            await Writes(Json)(chain);
        }, Filter());

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentLength));
    }

    /// <summary>
    /// A strong validator names one exact byte sequence, and a compressed body is a different one.
    /// Weakened rather than dropped, so a conditional request can still be answered.
    /// </summary>
    [Theory]
    [InlineData("\"abc\"", "W/\"abc\"")]
    [InlineData("W/\"abc\"", "W/\"abc\"")]
    public async Task AStrongETagBecomesWeakOnACompressedResponse(string before, string after) {
        var context = Context();

        await Run(context, async chain => {
            chain.Context.Response.Headers[KnownHeaders.ETag] = before;

            await Writes(Json)(chain);
        }, Filter());

        Assert.Equal(after, context.Response.Headers[KnownHeaders.ETag].ToString());
    }

    [Fact]
    public async Task AnETagIsLeftAloneOnAnIdentityResponse() {
        var context = Context(acceptEncoding: null);

        await Run(context, async chain => {
            chain.Context.Response.Headers[KnownHeaders.ETag] = "\"abc\"";

            await Writes(Json)(chain);
        }, Filter());

        Assert.Equal("\"abc\"", context.Response.Headers[KnownHeaders.ETag].ToString());
    }

    [Fact]
    public async Task ACompressedResponseIsMarkedBinaryForTheApiGatewayHost() {
        var compressed = Context();
        var plain = Context(acceptEncoding: null);

        await Run(compressed, Writes(Json), Filter());
        await Run(plain, Writes(Json), Filter());

        Assert.True(compressed.Response.IsBinary);
        Assert.False(plain.Response.IsBinary);
    }

    /// <summary>
    /// How the OpenAPI document and a precompressed static file are left alone without knowing
    /// about the filter: they set the header before they write.
    /// </summary>
    [Fact]
    public async Task AResponseAlreadyCarryingAContentEncodingIsPassedThrough() {
        var context = Context();
        var already = Encoding.UTF8.GetBytes("already gzip bytes");

        await Run(context, async chain => {
            chain.Context.Response.ContentType = "application/json";
            chain.Context.Response.Headers[KnownHeaders.ContentEncoding] = "gzip";

            await chain.Context.Response.Body.WriteAsync(already);
        }, Filter());

        Assert.Equal(already, Transport(context));
        Assert.Equal("gzip", Encoding_(context));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Vary));
    }

    [Theory]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(304)]
    public async Task AStatusWithNoBodyOrAByteRangeIsPassedThrough(int status) {
        var context = Context();

        await Run(context, Writes(Json, status: status), Filter());

        Assert.Equal("", Encoding_(context));
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(context)));
    }

    [Fact]
    public async Task AnErrorResponseIsCompressedLikeAnyOther() {
        var context = Context();

        await Run(context, Writes(Json, status: 500), Filter());

        Assert.Equal("gzip", Encoding_(context));
    }

    /// <summary>
    /// Nothing written is nothing to compress: no encoder is opened and no header written, so an
    /// empty 200 does not go out labelled as a gzip member of zero bytes.
    /// </summary>
    [Fact]
    public async Task AResponseThatWritesNothingCarriesNoCoding() {
        var context = Context();

        await Run(context, chain => {
            chain.Context.Response.ContentType = "application/json";

            return Task.CompletedTask;
        }, Filter());

        Assert.Equal("", Encoding_(context));
        Assert.Empty(Transport(context));
    }

    /// <summary>
    /// A flush before any write decides nothing, so the write that follows still gets the
    /// decision it would have had.
    /// </summary>
    [Fact]
    public async Task AFlushBeforeTheFirstWriteLeavesTheDecisionOpen() {
        var context = Context();

        await Run(context, async chain => {
            await chain.Context.Response.Body.FlushAsync();

            await Writes(Json)(chain);
        }, Filter());

        Assert.Equal("gzip", Encoding_(context));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.GZip));
    }

    // ---------------------------------------------------------------- the rule

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    [InlineData("text/event-stream")]
    public async Task AMediaTypeOutsideTheListIsPassedThrough(string contentType) {
        var context = Context();

        await Run(context, Writes(Json, contentType: contentType), Filter());

        Assert.Equal("", Encoding_(context));
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(context)));
    }

    [Fact]
    public async Task APredicateIsConsultedWithTheHandlersValue() {
        var context = Context();
        var predicate = new Predicate(answer: false);
        var value = new List<int> { 1, 2, 3 };

        await Run(context, Writes(Json, value: value), Filter(predicate));

        Assert.Same(value, Assert.Single(predicate.Seen));
        Assert.Equal("", Encoding_(context));
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(context)));
    }

    /// <summary>
    /// A predicate replaces the media-type rule for its operation, so it can opt in a type the
    /// default list leaves out.
    /// </summary>
    [Fact]
    public async Task APredicateCanOptInATypeTheListLeavesOut() {
        var context = Context();

        await Run(context, Writes(Json, contentType: "application/octet-stream", value: new object()), Filter(new Predicate(true)));

        Assert.Equal("gzip", Encoding_(context));
    }

    /// <summary>
    /// A hit replayed from the cache carries no handler value, so there is nothing to show a
    /// predicate and the default rule applies instead.
    /// </summary>
    [Fact]
    public async Task APredicateIsSkippedWhenTheResponseCarriesNoValue() {
        var context = Context();
        var predicate = new Predicate(answer: false);

        await Run(context, Writes(Json, value: null), Filter(predicate));

        Assert.Empty(predicate.Seen);
        Assert.Equal("gzip", Encoding_(context));
    }

    // ---------------------------------------------------------------- the body stream

    /// <summary>
    /// What the API Gateway host reads to decide whether the response has started, and what the
    /// testing response reads for the same purpose: bytes accepted, not bytes emitted.
    /// </summary>
    [Fact]
    public async Task TheBodyReportsItsPositionAsBytesAccepted() {
        var context = Context();
        long position = -1;

        await Run(context, async chain => {
            await Writes(Json)(chain);

            position = chain.Context.Response.Body.Position;
        }, Filter());

        Assert.Equal(Encoding.UTF8.GetByteCount(Json), position);
    }

    /// <summary>
    /// The synchronous members, which a view engine or a raw writer may use. They reach the same
    /// encoder as the asynchronous ones, and the wrapper is write-only.
    /// </summary>
    [Fact]
    public async Task TheSynchronousWritesReachTheSameEncoder() {
        var context = Context();
        var bytes = Encoding.UTF8.GetBytes(Json);

        await Run(context, chain => {
            var response = chain.Context.Response;
            var body = response.Body;

            response.ContentType = "application/json";

            Assert.True(body.CanWrite);
            Assert.False(body.CanRead);
            Assert.False(body.CanSeek);

            body.WriteByte(bytes[0]);
            body.Write(bytes, 1, 3);
            body.Write(bytes.AsSpan(4));
            body.Flush();

            Assert.Equal(bytes.Length, body.Length);

            Assert.Throws<NotSupportedException>(() => body.Position = 0);
            Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => body.SetLength(0));

            return Task.CompletedTask;
        }, Filter());

        Assert.Equal("gzip", Encoding_(context));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.GZip));
    }

    [Fact]
    public async Task TheTransportIsRestoredAfterTheChain() {
        var context = Context();
        var transport = context.Response.Body;

        await Run(context, Writes(Json), Filter());

        Assert.Same(transport, context.Response.Body);
    }

    /// <summary>
    /// A class-level and a method-level declaration both install a filter. The inner one finds the
    /// body already wrapped and stands down, so the bytes are compressed once.
    /// </summary>
    [Fact]
    public async Task ADoubleRegistrationWrapsOnce() {
        var context = Context();

        await Run(context, Writes(Json), Filter(), Filter(favor: CompressionType.Br));

        Assert.Equal("gzip", Encoding_(context));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.GZip));
    }

    // ---------------------------------------------------------------- the cache

    private sealed class FixedKey : ICacheKeyProvider {
        public static ICacheKeyProvider Create(string[] values) => new FixedKey();

        public ValueTask<string?> Key(IExecutionContext context) => new("fixed");
    }

    private sealed class RecordingStore : IResponseCacheStore {
        private readonly Dictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);

        public List<CachedResponse> Stored { get; } = [];

        public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) =>
            new(_entries.TryGetValue(key, out var entry) ? entry : null);

        public ValueTask Set(string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken) {
            _entries[key] = response;
            Stored.Add(response);

            return default;
        }

        public ValueTask EvictByTag(string tag, CancellationToken cancellationToken) => default;
    }

    private static (IServiceProvider Services, RecordingStore Store) Caching() {
        var store = new RecordingStore();
        var services = new ServiceCollection();

        services.AddSingleton<IResponseCacheStore>(store);

        return (services.BuildServiceProvider(), store);
    }

    private static ResponseCacheFilter Cache() =>
        new([FixedKey.Create([])], "GET /pets", duration: 60);

    /// <summary>
    /// The cache filter buffers the body inside the compression wrapper, so what it stores is the
    /// identity bytes and the coding header the wrapper wrote while the buffer was copied out is
    /// not part of the entry.
    /// </summary>
    [Fact]
    public async Task TheStoredEntryHoldsIdentityBytesAndNoCodingHeader() {
        var (services, store) = Caching();
        var context = Context(services: services);

        await Run(context, Writes(Json), Filter(), Cache());

        var entry = Assert.Single(store.Stored);

        Assert.Equal(Json, Encoding.UTF8.GetString(entry.Body));
        Assert.DoesNotContain(entry.Headers, header => header.Key == KnownHeaders.ContentEncoding);
        Assert.Equal("gzip", Encoding_(context));
        Assert.Equal(Json, Decode(Transport(context), KnownEncoding.GZip));
    }

    [Fact]
    public async Task AHitIsCompressedOnTheWayOut() {
        var (services, _) = Caching();
        var handled = 0;

        var miss = Context(services: services);
        var hit = Context(services: services);

        Func<IExecutionChain, Task> handler = async chain => {
            handled++;

            await Writes(Json)(chain);
        };

        await Run(miss, handler, Filter(), Cache());
        await Run(hit, handler, Filter(), Cache());

        Assert.Equal(1, handled);
        Assert.Equal("gzip", Encoding_(hit));
        Assert.Equal(Json, Decode(Transport(hit), KnownEncoding.GZip));
    }

    [Fact]
    public async Task AHitToAClientAcceptingNothingIsServedPlain() {
        var (services, _) = Caching();
        var miss = Context(services: services);
        var hit = Context(acceptEncoding: null, services: services);

        await Run(miss, Writes(Json), Filter(), Cache());
        await Run(hit, Writes(Json), Filter(), Cache());

        Assert.Equal("", Encoding_(hit));
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(hit)));
    }

    /// <summary>
    /// The miss was decided by the predicate. The hit has no handler value, so it is decided by
    /// the media-type rule - which is the documented behaviour, not an accident.
    /// </summary>
    [Fact]
    public async Task APredicateIsNotConsultedOnAHit() {
        var (services, _) = Caching();
        var predicate = new Predicate(answer: true);
        var miss = Context(services: services);
        var hit = Context(services: services);

        await Run(miss, Writes(Json, value: new object()), Filter(predicate), Cache());
        await Run(hit, Writes(Json, value: new object()), Filter(predicate), Cache());

        Assert.Single(predicate.Seen);
        Assert.Equal("gzip", Encoding_(hit));
    }

    // ---------------------------------------------------------------- streams

    private sealed class FlushRecordingStream : MemoryStream {
        public List<long> FlushedAt { get; } = [];

        public TaskCompletionSource FirstFlush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task FlushAsync(CancellationToken cancellationToken) {
            FlushedAt.Add(Length);
            FirstFlush.TrySetResult();

            return base.FlushAsync(cancellationToken);
        }
    }

    private static IExecutionContext StreamingContext(FlushRecordingStream transport) {
        var headers = new Dictionary<string, StringValues> { [KnownHeaders.AcceptEncoding] = Browser };
        var request = new TestExecutionRequest("GET", "/feed", "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) { Headers = headers };
        var provider = new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(provider, provider, Substitute.For<IKnownServices>(),
            request, new TestExecutionResponse(transport), CancellationToken.None);
    }

    private static AsyncEnumerableIoFilter<string> Streaming() =>
        new(
            _ => Task.FromResult(EmptyParameters.Instance),
            context => JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue),
            headerActions: null);

    private static async IAsyncEnumerable<string> Items(Task gate) {
        yield return "first";

        await gate;

        yield return "second";
    }

    private static string DecodeLeniently(byte[] bytes) {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var output = new MemoryStream();
        var buffer = new byte[256];

        try {
            int read;

            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                output.Write(buffer, 0, read);
            }
        }
        catch (IOException) {
            // A member cut off mid-stream still yields everything before the cut.
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// What the streaming plan deferred as a different change. The filter flushes the body after
    /// every item, a flush on the wrapper is a sync flush on the encoder, and so the stream is one
    /// gzip member whose first item a reader has before the second is produced.
    /// </summary>
    [Fact]
    public async Task AnNdjsonStreamIsOneMemberDeliveredItemByItem() {
        var transport = new FlushRecordingStream();
        var context = StreamingContext(transport);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = Run(context, chain => {
            chain.Context.Response.ResponseValue = Items(gate.Task);

            return Task.CompletedTask;
        }, Filter(), Streaming());

        await transport.FirstFlush.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var afterFirst = transport.ToArray();

        gate.SetResult();

        await run;

        Assert.Equal("application/x-ndjson", context.Response.ContentType);
        Assert.Equal("gzip", Encoding_(context));
        Assert.Contains("\"first\"", DecodeLeniently(afterFirst));
        Assert.DoesNotContain("\"second\"", DecodeLeniently(afterFirst));

        var whole = Decode(transport.ToArray(), KnownEncoding.GZip);

        Assert.Equal("\"first\"\n\"second\"\n\n", whole);
        Assert.Equal(1, CountMembers(transport.ToArray()));
    }

    /// <summary>
    /// Every gzip member opens with the same magic and method byte. Concatenated members - what
    /// compressing per item produced - would show it more than once.
    /// </summary>
    private static int CountMembers(byte[] bytes) {
        var count = 0;

        for (var i = 0; i + 2 < bytes.Length; i++) {
            if (bytes[i] == 0x1f && bytes[i + 1] == 0x8b && bytes[i + 2] == 0x08) {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task AnEventStreamIsPassedThrough() {
        var transport = new FlushRecordingStream();
        var context = StreamingContext(transport);
        var filter = new AsyncEnumerableIoFilter<string>(
            _ => Task.FromResult(EmptyParameters.Instance),
            c => JsonSerializer.SerializeAsync(c.Response.Body, c.Response.ResponseValue),
            headerActions: null,
            framing: SseFraming.Instance);

        await Run(context, chain => {
            chain.Context.Response.ResponseValue = Items(Task.CompletedTask);

            return Task.CompletedTask;
        }, Filter(), filter);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal("", Encoding_(context));
        Assert.Contains("\"first\"", Encoding.UTF8.GetString(transport.ToArray()));
    }
}
