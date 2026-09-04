using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Compression;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Compression;
using Hardened.Web.Runtime.Conditional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Conditional;

/// <summary>
/// The conditional-request filter, driven with real request, response and chain objects and a
/// stage in the handler's position that sets what a handler would and writes what a serializer
/// would.
///
/// <para>
/// Whether the request is conditional is decided at filter entry and asserted through whether
/// the body was wrapped at all; whether the caller holds the representation is decided on the
/// first write and asserted through the status, the bytes that reach the transport and the
/// headers beside them.
/// </para>
/// </summary>
public class ConditionalRequestFilterTests {

    private const string Json = """{"base":"USD","rates":{"EUR":0.92,"GBP":0.79}}""";

    private const string Tag = "\"OybX3FuqNfSKoSm+h1FJqQ==\"";

    private const string Stale = "\"stale\"";

    private static readonly DateTimeOffset Noon = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- fixtures

    private static IExecutionContext Context(
        string method = "GET",
        string? ifNoneMatch = null,
        string? ifModifiedSince = null,
        string? acceptEncoding = null,
        IServiceProvider? services = null) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (ifNoneMatch != null) {
            headers[KnownHeaders.IfNoneMatch] = ifNoneMatch;
        }

        if (ifModifiedSince != null) {
            headers[KnownHeaders.IfModifiedSince] = ifModifiedSince;
        }

        if (acceptEncoding != null) {
            headers[KnownHeaders.AcceptEncoding] = acceptEncoding;
        }

        return Context(method, headers, services);
    }

    private static IExecutionContext Context(
        string method, Dictionary<string, StringValues> headers, IServiceProvider? services = null) {
        var request = new TestExecutionRequest(
            method, "/rates", "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Headers = headers
        };

        var provider = services ?? new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            provider, provider, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    /// <summary>
    /// Runs the filters over a final stage standing in for the handler and the serializer.
    /// </summary>
    private static async Task Run(
        IExecutionContext context, Func<IExecutionChain, Task> handler, params IExecutionFilter[] filters) {
        var chain = filters.Select<IExecutionFilter, Func<IExecutionContext, IExecutionFilter>>(filter => _ => filter)
            .Append(_ => new Stage(handler))
            .ToList();

        await new ExecutionChain(chain, context).Next();
    }

    /// <summary>
    /// A handler that sets what a handler and a serializer would, then writes <paramref name="body"/>.
    /// </summary>
    private static Func<IExecutionChain, Task> Writes(
        string body,
        string? etag = Tag,
        string? lastModified = null,
        int? status = null,
        string contentType = "application/json") =>
        async chain => {
            var response = chain.Context.Response;

            response.Status = status;
            response.ContentType = contentType;
            response.Headers[KnownHeaders.CacheControl] = "public, max-age=60";

            if (etag != null) {
                response.Headers[KnownHeaders.ETag] = etag;
            }

            if (lastModified != null) {
                response.Headers[KnownHeaders.LastModified] = lastModified;
            }

            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(body));
        };

    private static byte[] Transport(IExecutionContext context) =>
        ((MemoryStream)context.Response.Body).ToArray();

    private static string Header(IExecutionContext context, string name) =>
        context.Response.Headers.TryGetValue(name, out var value) ? value.ToString() : "";

    private static void AssertNotModified(IExecutionContext context, string etag = Tag) {
        Assert.Equal(304, context.Response.Status);
        Assert.Empty(Transport(context));

        // What RFC 9110 §15.4.5 says a 304 carries when a 200 would have.
        Assert.Equal(etag, Header(context, KnownHeaders.ETag));
        Assert.Equal("public, max-age=60", Header(context, KnownHeaders.CacheControl));

        // And what describes content it does not have.
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentType));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentLength));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentEncoding));
    }

    private static void AssertSentInFull(IExecutionContext context, int? status = null) {
        Assert.Equal(status, context.Response.Status);
        Assert.Equal(Json, Encoding.UTF8.GetString(Transport(context)));
        Assert.Equal("application/json", context.Response.ContentType);
    }

    private sealed class Stage : IExecutionFilter {
        private readonly Func<IExecutionChain, Task> _body;

        public Stage(Func<IExecutionChain, Task> body) {
            _body = body;
        }

        public Task Execute(IExecutionChain chain) => _body(chain);
    }

    // ---------------------------------------------------------------- entry

    /// <summary>
    /// The ordinary request. Nothing is wrapped, so nothing is paid.
    /// </summary>
    [Fact]
    public async Task AnUnconditionalRequestIsNotWrapped() {
        var context = Context();
        var transport = context.Response.Body;
        Stream? seen = null;

        await Run(context, async chain => {
            seen = chain.Context.Response.Body;

            await Writes(Json)(chain);
        }, new ConditionalRequestFilter());

        Assert.Same(transport, seen);
        AssertSentInFull(context);
    }

    /// <summary>
    /// The conditionals mean a 412 on any other method, which is not evaluated here, so the
    /// request is left alone rather than half-handled.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task AMethodOtherThanGetOrHeadIsPassedThrough(string method) {
        var context = Context(method, ifNoneMatch: Tag);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    /// <summary>
    /// A HEAD reaches the GET handler and is revalidated the same way. The bytes it would have
    /// discarded are the bytes a 304 does not send.
    /// </summary>
    [Theory]
    [InlineData("HEAD")]
    [InlineData("head")]
    public async Task AHeadIsRevalidatedLikeAGet(string method) {
        var context = Context(method, ifNoneMatch: Tag);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    /// <summary>
    /// A response something ahead of this stage already started cannot change its status, so it
    /// is not wrapped either.
    /// </summary>
    [Fact]
    public async Task AResponseThatAlreadyStartedIsLeftAlone() {
        var context = Context(ifNoneMatch: Tag);
        var transport = context.Response.Body;
        Stream? seen = null;

        transport.WriteByte((byte)' ');

        await Run(context, async chain => {
            seen = chain.Context.Response.Body;

            await Writes(Json)(chain);
        }, new ConditionalRequestFilter());

        Assert.Same(transport, seen);
        Assert.Null(context.Response.Status);
        Assert.Equal(" " + Json, Encoding.UTF8.GetString(Transport(context)));
    }

    /// <summary>
    /// API Gateway delivers header names lowercased.
    /// </summary>
    [Fact]
    public async Task TheConditionalHeadersAreReadWhateverTheirCase() {
        var headers = new Dictionary<string, StringValues> { ["if-none-match"] = Tag };
        var context = Context("GET", headers);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    // ---------------------------------------------------------------- If-None-Match

    [Fact]
    public async Task AMatchingTagIsAnswered304WithNoBody() {
        var context = Context(ifNoneMatch: Tag);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    /// <summary>
    /// Weak comparison, and the whole list, per RFC 9110 §13.1.2 - the rule is
    /// <c>EntityTagHeader</c>'s and this only checks it is the rule being applied.
    /// </summary>
    [Theory]
    [InlineData("W/" + Tag)]
    [InlineData("\"one\", " + Tag + ", \"three\"")]
    public async Task AWeakOrListedTagStillMatches(string ifNoneMatch) {
        var context = Context(ifNoneMatch: ifNoneMatch);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    [Fact]
    public async Task AStaleTagIsAnsweredInFull() {
        var context = Context(ifNoneMatch: Stale);

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertSentInFull(context);
        Assert.Equal(Tag, Header(context, KnownHeaders.ETag));
    }

    /// <summary>
    /// Nothing here computes a validator, so a response carrying none is sent in full whatever the
    /// caller claims to hold.
    /// </summary>
    [Theory]
    [InlineData(Tag)]
    [InlineData("*")]
    public async Task AResponseWithNoValidatorIsSentInFull(string ifNoneMatch) {
        var context = Context(ifNoneMatch: ifNoneMatch);

        await Run(context, Writes(Json, etag: null), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    // ---------------------------------------------------------------- If-Modified-Since

    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    public async Task IfModifiedSinceAtOrAfterLastModifiedIsAnswered304(int secondsLater) {
        var context = Context(ifModifiedSince: HttpDate.Format(Noon.AddSeconds(secondsLater)));

        await Run(context, Writes(Json, lastModified: HttpDate.Format(Noon)), new ConditionalRequestFilter());

        AssertNotModified(context);
        Assert.Equal(HttpDate.Format(Noon), Header(context, KnownHeaders.LastModified));
    }

    [Fact]
    public async Task IfModifiedSinceBeforeLastModifiedIsAnsweredInFull() {
        var context = Context(ifModifiedSince: HttpDate.Format(Noon.AddSeconds(-1)));

        await Run(context, Writes(Json, lastModified: HttpDate.Format(Noon)), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    /// <summary>
    /// A validator is a stronger statement than a timestamp, and a client that sent both meant the
    /// validator: a stale tag is a full body even when the date alone would have been a 304.
    /// </summary>
    [Fact]
    public async Task AStaleTagOutranksASatisfiedDate() {
        var context = Context(ifNoneMatch: Stale, ifModifiedSince: HttpDate.Format(Noon));

        await Run(context, Writes(Json, lastModified: HttpDate.Format(Noon)), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    [Fact]
    public async Task IfModifiedSinceAgainstAResponseWithNoLastModifiedIsAnsweredInFull() {
        var context = Context(ifModifiedSince: HttpDate.Format(Noon));

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    // ---------------------------------------------------------------- what is never a 304

    /// <summary>
    /// A 304 stands in for a 200 and nothing else. A 404 or a 500 is about the moment, a 201 or a
    /// 204 is not a representation the caller could hold.
    /// </summary>
    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task AStatusOtherThan200IsPassedThrough(int status) {
        var context = Context(ifNoneMatch: Tag);

        await Run(context, Writes(Json, status: status), new ConditionalRequestFilter());

        AssertSentInFull(context, status);
    }

    /// <summary>
    /// A refusal recorded ahead of serialization travels on with the request and is written
    /// behind this stage. A 304 in its place would tell a caller who may not read the resource
    /// that it has not changed.
    /// </summary>
    [Fact]
    public async Task ARefusedRequestIsNotAnswered304() {
        var context = Context(ifNoneMatch: Tag);

        context.Response.ExceptionValue = new UnauthorizedAccessException("no grant");

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    // ---------------------------------------------------------------- when the decision is made

    /// <summary>
    /// A response that writes no body - a handler returning nothing, an empty 200 - never makes
    /// the first write, so it is decided as the chain returns.
    /// </summary>
    [Fact]
    public async Task AResponseThatWritesNothingIsDecidedAsTheChainReturns() {
        var context = Context(ifNoneMatch: Tag);

        await Run(context, chain => {
            var response = chain.Context.Response;

            response.Status = 200;
            response.ContentType = "application/json";
            response.Headers[KnownHeaders.ETag] = Tag;
            response.Headers[KnownHeaders.CacheControl] = "public, max-age=60";

            return Task.CompletedTask;
        }, new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    /// <summary>
    /// A flush before any write decides, because on Kestrel a flush starts the response and a
    /// status changed after that throws. The validator is on the response by then or never.
    /// </summary>
    [Fact]
    public async Task AFlushBeforeTheFirstWriteDecides() {
        var context = Context(ifNoneMatch: Tag);

        await Run(context, async chain => {
            var response = chain.Context.Response;

            response.ContentType = "application/json";
            response.Headers[KnownHeaders.ETag] = Tag;
            response.Headers[KnownHeaders.CacheControl] = "public, max-age=60";

            await response.Body.FlushAsync();
            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(Json));
        }, new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    /// <summary>
    /// A chain that threw is answered by whatever catches it. Deciding a 304 underneath the
    /// failure would set a status the host then overwrites, so nothing is decided and the
    /// transport is put back for the error path.
    /// </summary>
    [Fact]
    public async Task AThrowingChainIsNotDecidedAndPutsTheTransportBack() {
        var context = Context(ifNoneMatch: Tag);
        var transport = context.Response.Body;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(context, chain => {
            chain.Context.Response.Headers[KnownHeaders.ETag] = Tag;

            throw new InvalidOperationException("handler failed");
        }, new ConditionalRequestFilter()));

        Assert.Null(context.Response.Status);
        Assert.Same(transport, context.Response.Body);
    }

    [Fact]
    public async Task TheTransportIsRestoredAfterTheChain() {
        var context = Context(ifNoneMatch: Stale);
        var transport = context.Response.Body;

        await Run(context, Writes(Json), new ConditionalRequestFilter());

        Assert.Same(transport, context.Response.Body);
    }

    /// <summary>
    /// A filter registered twice on one handler finds the body already wrapped and stands down,
    /// so the request is decided once.
    /// </summary>
    [Fact]
    public async Task ADoubleRegistrationWrapsOnce() {
        var context = Context(ifNoneMatch: Tag);

        await Run(context, Writes(Json), new ConditionalRequestFilter(), new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    // ---------------------------------------------------------------- the body stream

    /// <summary>
    /// The synchronous members, which a view engine or a raw writer may use, and the flushes. They
    /// reach the transport untouched when the caller does not hold the representation, and the
    /// wrapper is write-only.
    /// </summary>
    [Fact]
    public async Task EveryWritePathReachesTheTransportWhenTheCallerHoldsNothing() {
        var context = Context(ifNoneMatch: Stale);
        var bytes = Encoding.UTF8.GetBytes(Json);

        await Run(context, async chain => {
            var response = chain.Context.Response;
            var body = response.Body;

            response.ContentType = "application/json";
            response.Headers[KnownHeaders.ETag] = Tag;

            Assert.True(body.CanWrite);
            Assert.False(body.CanRead);
            Assert.False(body.CanSeek);

            body.Flush();
            body.WriteByte(bytes[0]);
            body.Write(bytes, 1, 3);
            body.Write(bytes.AsSpan(4, 5));
            await body.WriteAsync(bytes, 9, 7, CancellationToken.None);
            await body.WriteAsync(bytes.AsMemory(16));
            await body.FlushAsync();

            Assert.Equal(bytes.Length, body.Length);
            Assert.Equal(bytes.Length, body.Position);

            Assert.Throws<NotSupportedException>(() => body.Position = 0);
            Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => body.SetLength(0));
        }, new ConditionalRequestFilter());

        AssertSentInFull(context);
    }

    /// <summary>
    /// Everything written after a 304 is dropped, through every path there is. The count still
    /// advances, because it is what the testing response reads to decide the response started.
    /// </summary>
    [Fact]
    public async Task EveryWritePathIsDroppedAfterA304() {
        var context = Context(ifNoneMatch: Tag);
        var bytes = Encoding.UTF8.GetBytes(Json);

        await Run(context, async chain => {
            var response = chain.Context.Response;
            var body = response.Body;

            response.ContentType = "application/json";
            response.Headers[KnownHeaders.ETag] = Tag;
            response.Headers[KnownHeaders.CacheControl] = "public, max-age=60";

            body.WriteByte(bytes[0]);
            body.Write(bytes, 1, 3);
            body.Write(bytes.AsSpan(4, 5));
            await body.WriteAsync(bytes, 9, 7, CancellationToken.None);
            await body.WriteAsync(bytes.AsMemory(16));
            body.Flush();
            await body.FlushAsync();

            Assert.Equal(bytes.Length, body.Position);
        }, new ConditionalRequestFilter());

        AssertNotModified(context);
    }

    // ---------------------------------------------------------------- compression

    private static ResponseCompressionFilter Compression() =>
        new(configuration: new CompressionConfiguration());

    /// <summary>
    /// The compressing body sits one stage inside this one and writes its coding on its own first
    /// write - which is the write this decides on. A 304 has nothing to encode, so the coding
    /// comes off with the other content headers and the encoder's trailer is dropped with the rest.
    /// The tag stays weak, because that is the tag the 200 would have carried through the same
    /// encoder, and <c>Vary</c> stays for the same reason.
    /// </summary>
    [Fact]
    public async Task A304ThroughTheCompressingBodyCarriesNoCodingAndNoBytes() {
        var context = Context(ifNoneMatch: Tag, acceptEncoding: "gzip");

        await Run(context, Writes(Json), new ConditionalRequestFilter(), Compression());

        AssertNotModified(context, etag: "W/" + Tag);
        Assert.Equal("Accept-Encoding", Header(context, KnownHeaders.Vary));
    }

    /// <summary>
    /// And a full answer through the same pair is the compressed body it always was, with the
    /// strong tag weakened by the encoder as before.
    /// </summary>
    [Fact]
    public async Task AStaleTagThroughTheCompressingBodyIsGzipped() {
        var context = Context(ifNoneMatch: Stale, acceptEncoding: "gzip");

        await Run(context, Writes(Json), new ConditionalRequestFilter(), Compression());

        Assert.Equal("gzip", Header(context, KnownHeaders.ContentEncoding));
        Assert.Equal("W/" + Tag, Header(context, KnownHeaders.ETag));
        Assert.Equal(Json, Decode(Transport(context)));
    }

    private static string Decode(byte[] bytes) {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    // ---------------------------------------------------------------- the response cache

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
        new([FixedKey.Create([])], "GET /rates", duration: 60);

    /// <summary>
    /// The whole point of the ordering: a hit is revalidated because this stage is outside the
    /// cache. The tag the client holds is the one the cache put on the miss, and the hit is a
    /// 304 that neither ran the handler nor sent the stored bytes.
    /// </summary>
    [Fact]
    public async Task AHitWithTheTagTheMissCarriedIs304WithoutTheStoredBody() {
        var (services, store) = Caching();
        var handled = 0;

        Func<IExecutionChain, Task> handler = async chain => {
            handled++;

            await Writes(Json, etag: null)(chain);
        };

        var miss = Context(services: services);

        await Run(miss, handler, new ConditionalRequestFilter(), Cache());

        var tag = Header(miss, KnownHeaders.ETag);

        Assert.StartsWith("\"", tag);
        Assert.Single(store.Stored);

        var hit = Context(ifNoneMatch: tag, services: services);

        await Run(hit, handler, new ConditionalRequestFilter(), Cache());

        Assert.Equal(1, handled);
        Assert.Equal(304, hit.Response.Status);
        Assert.Empty(Transport(hit));
        Assert.Equal(tag, Header(hit, KnownHeaders.ETag));
        Assert.False(hit.Response.Headers.ContainsKey(KnownHeaders.ContentType));
    }

    /// <summary>
    /// A miss with a matching tag - the client holds it from before the store was emptied - still
    /// runs the handler and fills the store, and still sends no body.
    /// </summary>
    [Fact]
    public async Task AMissWithAMatchingTagIs304AndStillFillsTheStore() {
        var (services, store) = Caching();
        var probe = Context(services: services);

        await Run(probe, Writes(Json, etag: null), new ConditionalRequestFilter(), Cache());

        var tag = Header(probe, KnownHeaders.ETag);
        var (again, emptied) = Caching();
        var miss = Context(ifNoneMatch: tag, services: again);

        await Run(miss, Writes(Json, etag: null), new ConditionalRequestFilter(), Cache());

        Assert.Single(store.Stored);
        Assert.Single(emptied.Stored);
        Assert.Equal(304, miss.Response.Status);
        Assert.Empty(Transport(miss));
    }
}
