using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Compression;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Compression;
using Hardened.Web.Runtime.Conditional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// A 304 over a real Kestrel socket, behind the three filters in the order the pipeline runs
/// them: conditional, then compression, then the response cache.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory test client cannot see what matters here. A 304 has no body, and on Kestrel
/// writing one throws; the status is decided on the first write, and the headers it changes have
/// to be changeable when Kestrel sends them; and a 304 the host framed wrongly leaves the client
/// waiting for a body that never comes, which only a connection reused for the next request
/// shows.
/// </para>
/// <para>
/// No routing and no generated handler, as in the response-cache test beside this one: the chain
/// is the three filters and something terminal to answer, which is enough to put the decision on
/// the wire.
/// </para>
/// </remarks>
public class ConditionalGetOverASocketTests {

    private const string Answer = """{"base":"USD","rates":{"EUR":0.92,"GBP":0.79}}""";

    /// <summary>
    /// The miss tags the entry with a strong validator, and a plain client is handed it as such.
    /// </summary>
    [Fact]
    public async Task AMissCarriesAStrongTagAndAHitTheSameOne() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);
        var hit = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);

        var tag = Assert.IsType<System.Net.Http.Headers.EntityTagHeaderValue>(miss.Headers.ETag);

        Assert.False(tag.IsWeak);
        Assert.Equal(tag, hit.Headers.ETag);
    }

    [Fact]
    public async Task AClientHoldingTheTagIsAnswered304WithNoBody() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);
        var tag = miss.Headers.ETag!.ToString();

        using var revalidated = await harness.Revalidate(tag, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        AssertNoContentHeaders(revalidated);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(tag, revalidated.Headers.ETag!.ToString());
        Assert.Equal(1, harness.Answered);
    }

    /// <summary>
    /// The framing check. The client holds one connection, so a 304 that declared a length or let
    /// the stored bytes through leaves the next request on it reading the wrong thing - and a
    /// client that gets the whole answer afterwards saw a 304 the host framed itself.
    /// </summary>
    [Fact]
    public async Task TheConnectionIsReusableAfterA304() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);
        var tag = miss.Headers.ETag!.ToString();

        using (var revalidated = await harness.Revalidate(tag, cancellationToken: TestContext.Current.CancellationToken)) {
            Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        }

        var hit = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        Assert.Equal(Answer, await hit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The compressing body sits inside the conditional one and had already written its coding
    /// and weakened the tag when the 304 was decided. The coding comes off, because there is no
    /// content for it to describe; the weak tag stays, because it is what the 200 carried.
    /// </summary>
    [Fact]
    public async Task A304ToAGzipClientCarriesNoCodingAndTheWeakTag() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(acceptEncoding: "gzip", cancellationToken: TestContext.Current.CancellationToken);
        var tag = miss.Headers.ETag!;

        Assert.True(tag.IsWeak);
        Assert.Equal("gzip", Assert.Single(miss.Content.Headers.ContentEncoding));

        using var revalidated = await harness.Revalidate(
            tag.ToString(), acceptEncoding: "gzip", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        AssertNoContentHeaders(revalidated);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(tag, revalidated.Headers.ETag);
        Assert.Contains("Accept-Encoding", revalidated.Headers.Vary);
    }

    [Fact]
    public async Task AHeadHoldingTheTagIs304WithoutALength() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(cancellationToken: TestContext.Current.CancellationToken);
        var tag = miss.Headers.ETag!.ToString();

        using var head = await harness.Revalidate(tag, HttpMethod.Head, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, head.StatusCode);
        AssertNoContentHeaders(head);
        Assert.Equal(tag, head.Headers.ETag!.ToString());
    }

    /// <summary>
    /// Asked of the headers as they arrived, before the body is read. Reading it buffers the
    /// content, and <c>HttpClient</c> then records the buffer's length in this collection as
    /// though a <c>Content-Length</c> had been on the wire.
    /// </summary>
    private static void AssertNoContentHeaders(HttpResponseMessage response) {
        Assert.False(response.Content.Headers.Contains("Content-Length"));
        Assert.False(response.Content.Headers.Contains("Content-Type"));
        Assert.False(response.Content.Headers.Contains("Content-Encoding"));
    }

    private sealed class Harness : IAsyncDisposable {
        private readonly HttpClient _client;

        private HardenedKestrelApplication _app = null!;

        private Harness(HttpClient client) {
            _client = client;
        }

        /// <summary>How many times the terminal filter answered, so a hit is provable.</summary>
        public int Answered { get; private set; }

        public static async Task<Harness> Start(CancellationToken cancellationToken) {
            // No automatic decompression, so the coding header and the bytes arrive as sent; one
            // connection, so every request is framed by the one before it; and a short timeout,
            // because the failure this exists for is a hang.
            var harness = new Harness(new HttpClient(new SocketsHttpHandler {
                AutomaticDecompression = DecompressionMethods.None,
                MaxConnectionsPerServer = 1
            }) { Timeout = TimeSpan.FromSeconds(10) });

            harness._app = Build();

            harness.Compose();

            await harness._app.StartAsync(cancellationToken);

            harness._client.BaseAddress = new Uri(harness._app.Addresses.First());

            return harness;
        }

        /// <summary>A full answer, read to the end so the connection is free for the next.</summary>
        public Task<HttpResponseMessage> Get(
            string? acceptEncoding = null, CancellationToken cancellationToken = default) =>
            Send(HttpMethod.Get, acceptEncoding, null, HttpCompletionOption.ResponseContentRead, cancellationToken);

        /// <summary>
        /// A request holding <paramref name="ifNoneMatch"/>, returned as soon as the headers are
        /// in, so a test can see them as they arrived. The caller disposes it.
        /// </summary>
        public Task<HttpResponseMessage> Revalidate(
            string ifNoneMatch,
            HttpMethod? method = null,
            string? acceptEncoding = null,
            CancellationToken cancellationToken = default) =>
            Send(method ?? HttpMethod.Get, acceptEncoding, ifNoneMatch,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        private async Task<HttpResponseMessage> Send(
            HttpMethod method,
            string? acceptEncoding,
            string? ifNoneMatch,
            HttpCompletionOption completion,
            CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(method, "/rates");

            if (acceptEncoding != null) {
                request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            }

            if (ifNoneMatch != null) {
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            }

            return await _client.SendAsync(request, completion, cancellationToken);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }

        private static HardenedKestrelApplication Build() {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

            new KestrelRuntime().PopulateServiceCollection(services);

            // The cache filter resolves this from the root provider on its first request.
            services.AddSingleton<IResponseCacheStore>(new Store());

            // Port 0, so the OS picks one and concurrent test classes cannot collide.
            return HardenedKestrelApplication.Create(
                services, kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        }

        /// <summary>
        /// The three filters in pipeline order, then the filter that answers.
        /// </summary>
        private void Compose() {
            var middleware = _app.Services.GetRequiredService<IMiddlewareService>();

            middleware.Use(_ => new ConditionalGetFilter());
            middleware.Use(_ => new ResponseCompressionFilter(configuration: new CompressionConfiguration()));
            middleware.Use(_ => new ResponseCacheFilter(
                [new EveryRequest()], "GET /rates", ResponseCacheFilter.DefaultDuration));
            middleware.Use(_ => new Answering(this));
        }

        private sealed class EveryRequest : ICacheKeyProvider {
            public static ICacheKeyProvider Create(string[] values) => new EveryRequest();

            public ValueTask<string?> Key(IExecutionContext context) => new("only");
        }

        /// <summary>
        /// Writes the answer and stops. Nothing sets a validator, so the one on the wire is the
        /// cache's.
        /// </summary>
        private sealed class Answering : IExecutionFilter {
            private readonly Harness _harness;

            public Answering(Harness harness) {
                _harness = harness;
            }

            public async Task Execute(IExecutionChain chain) {
                var response = chain.Context.Response;

                _harness.Answered++;

                response.Status = 200;
                response.ContentType = "application/json";
                response.ShouldSerialize = false;

                await response.Body.WriteAsync(
                    Encoding.UTF8.GetBytes(Answer), chain.Context.CancellationToken);
            }
        }

        private sealed class Store : IResponseCacheStore {
            private readonly Dictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);

            public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) =>
                new(_entries.GetValueOrDefault(key));

            public ValueTask Set(
                string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken) {
                _entries[key] = response;

                return default;
            }

            public ValueTask EvictByTag(string tag, CancellationToken cancellationToken) => default;
        }
    }
}
