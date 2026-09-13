using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Responses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// A response-cache hit over a real socket, which is the only place two of its defects were
/// visible.
/// </summary>
/// <remarks>
/// <para>
/// Written after the 0.19.0-rc1000 trial, where three arms and 311 green tests - at least 24 of
/// them asserting cache hits - missed both. <c>ITestWebApp</c> has no transport, so it never sets
/// a transfer encoding and never frames a body; the framework's own response-cache SUT is driven
/// through that harness, so nothing in this repository put a cached response on a wire.
/// </para>
/// <para>
/// Deliberately no routing and no generated handler. The chain is two middleware filters - the
/// cache, then something terminal to answer - which is enough to exercise the transport and keeps
/// this test in the project that owns the Kestrel host. Everything the defects needed comes from
/// the host: Kestrel frames a body with no <c>Content-Length</c> as chunked, and
/// <see cref="MiddlewareService"/> seeds <see cref="CorrelationHeaderFilter"/> ahead of anything
/// an application registers.
/// </para>
/// </remarks>
public class ResponseCacheOverASocketTests
{
    /// <summary>Long enough that a body silently truncated to nothing is unmistakable.</summary>
    private const string Answer = """{"base":"USD","rates":{"EUR":0.92,"GBP":0.79}}""";

    /// <summary>
    /// The malformed hit. Kestrel answered <c>200 OK</c> with correct headers and zero body bytes;
    /// ASP.NET Core answered a chunk-length parse error. The entry had captured the host's own
    /// <c>Transfer-Encoding: chunked</c>, so the hit re-declared chunked framing and then wrote the
    /// stored bytes with no chunk header and no terminator.
    /// </summary>
    [Fact]
    public async Task AHitCarriesTheWholeBodyTheMissCarried()
    {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Get(TestContext.Current.CancellationToken);
        var hit = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(Answer, miss);
        Assert.Equal(Answer, hit);
    }

    /// <summary>
    /// And the handler ran once, so the second answer really was the store's.
    /// </summary>
    [Fact]
    public async Task AHitDoesNotRunTheHandler()
    {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        await harness.Get(TestContext.Current.CancellationToken);
        await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Answered);
    }

    /// <summary>
    /// A cache hit carries the id of the request it answered, not the id of the request that
    /// filled the entry.
    /// </summary>
    /// <remarks>
    /// <see cref="CorrelationHeaderFilter"/> is seeded into every host's middleware chain and sets
    /// the header on the way in, ahead of the cache - so it runs on a hit as well as on a miss and
    /// the current request's id is already on the response. Capturing it froze the first caller's
    /// id onto everyone else's response for the whole duration, which is a caller quoting somebody
    /// else's request in a support ticket.
    /// </remarks>
    [Fact]
    public async Task AHitCarriesTheCallersOwnCorrelationId()
    {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var miss = await harness.Response(TestContext.Current.CancellationToken);
        var hit = await harness.Response(TestContext.Current.CancellationToken);

        var first = miss.Headers.GetValues(CorrelationHeaderFilter.HeaderName).Single();
        var second = hit.Headers.GetValues(CorrelationHeaderFilter.HeaderName).Single();

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// A hop-by-hop header is the host's to decide on the hit, as it was on the miss.
    /// </summary>
    [Fact]
    public async Task AHitIsFramedByTheHostRatherThanByTheStore()
    {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        await harness.Get(TestContext.Current.CancellationToken);

        var entry = Assert.Single(harness.Stored);

        Assert.DoesNotContain(
            entry.Headers,
            header =>
                string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>The two representations a negotiating operation in the trial declared.</summary>
    private static readonly string[] Negotiated = ["application/json", "application/x-msgpack"];

    /// <summary>
    /// The blocker, on a wire. Whoever filled the entry decided what everyone after got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A JSON caller filled the entry and the next caller, asking for
    /// <c>application/x-msgpack</c>, was handed the JSON bytes under
    /// <c>Content-Type: application/json</c> - not a miss and not a 406, a 200 carrying the
    /// representation it had said it could not read. And the other way round: a JSON caller after a
    /// MessagePack one got binary.
    /// </para>
    /// <para>
    /// Both orders, because the defect was symmetric and a test fixing one order would have passed
    /// against the bug half the time.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("application/json", "application/x-msgpack")]
    [InlineData("application/x-msgpack", "application/json")]
    public async Task ACallerGetsTheRepresentationItAskedFor(string first, string second)
    {
        await using var harness = await Harness.Start(
            TestContext.Current.CancellationToken,
            declared: Negotiated
        );

        var filled = await harness.Response(TestContext.Current.CancellationToken, first);
        var next = await harness.Response(TestContext.Current.CancellationToken, second);

        Assert.Equal(first, filled.Content.Headers.ContentType?.MediaType);
        Assert.Equal(second, next.Content.Headers.ContentType?.MediaType);

        Assert.Equal(
            "body for " + second,
            await next.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }

    /// <summary>
    /// And each representation is still cached: asking for one twice runs the handler once.
    /// </summary>
    [Fact]
    public async Task EachRepresentationIsStillCached()
    {
        await using var harness = await Harness.Start(
            TestContext.Current.CancellationToken,
            declared: Negotiated
        );

        await harness.Get(TestContext.Current.CancellationToken, "application/json");
        await harness.Get(TestContext.Current.CancellationToken, "application/x-msgpack");
        await harness.Get(TestContext.Current.CancellationToken, "application/json");
        await harness.Get(TestContext.Current.CancellationToken, "application/x-msgpack");

        Assert.Equal(2, harness.Answered);
    }

    /// <summary>
    /// <c>Vary: Accept</c> goes out with it, so the shared cache in front of the service keys the
    /// way the store does.
    /// </summary>
    /// <remarks>
    /// Keying correctly settles this service's own store and says nothing to a CDN, which would go
    /// on serving one representation to every client. On the hit as well, because it is a fact
    /// about the operation rather than about the response that filled the entry.
    /// </remarks>
    [Fact]
    public async Task ANegotiatedResponseSaysItVariesOnAccept()
    {
        await using var harness = await Harness.Start(
            TestContext.Current.CancellationToken,
            declared: Negotiated
        );

        var miss = await harness.Response(
            TestContext.Current.CancellationToken,
            "application/json"
        );

        var hit = await harness.Response(TestContext.Current.CancellationToken, "application/json");

        Assert.Equal("Accept", Assert.Single(miss.Headers.GetValues(KnownHeaders.Vary)));
        Assert.Equal("Accept", Assert.Single(hit.Headers.GetValues(KnownHeaders.Vary)));
    }

    /// <summary>
    /// An operation with one representation says nothing about Vary, because nothing varies and
    /// Vary is what a shared cache fragments on.
    /// </summary>
    [Fact]
    public async Task AnOperationWithOneRepresentationDoesNotVary()
    {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var response = await harness.Response(TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains(KnownHeaders.Vary));
    }

    /// <summary>
    /// A handler that declares caching in an application with no store answers the framework's
    /// error envelope rather than a 500 with nothing in it.
    /// </summary>
    /// <remarks>
    /// It answered <c>500</c> and <c>Content-Length: 0</c>. The filter threw at
    /// <c>FilterOrder.ResponseCache</c>, one stage ahead of the filter that turns a failure into
    /// bytes, so the failure unwound past the only thing that would have written a body. Recording
    /// it and continuing is the rule for that side of the line, and the envelope is what a caller
    /// gets from every other server fault. The message that names the handler and the package to
    /// reference stays in the log, where an unexpected 500's detail belongs.
    /// </remarks>
    [Fact]
    public async Task NoRegisteredStoreAnswersAnEnvelopeRatherThanAnEmptyFiveHundred()
    {
        await using var harness = await Harness.Start(
            TestContext.Current.CancellationToken,
            withStore: false
        );

        var response = await harness.Raw(TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Contains("ServerError", body);
    }

    /// <summary>
    /// A Hardened application on Kestrel, listening on a port the OS picked, answering one route
    /// from behind a response cache.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly HttpClient _client;

        private HardenedKestrelApplication _app = null!;

        private Harness(HttpClient client)
        {
            _client = client;
        }

        /// <summary>How many times the terminal filter answered, so a hit is provable.</summary>
        public int Answered { get; private set; }

        /// <summary>Every entry the store was handed, for asserting on what was captured.</summary>
        public List<CachedResponse> Stored { get; } = [];

        /// <param name="withStore">
        /// False composes the application the way an author who declared [CacheResponse] and
        /// referenced no store package composed theirs.
        /// </param>
        /// <param name="declared">
        /// What the operation produces, for the negotiating case. Empty is the single-representation
        /// operation every other test here drives.
        /// </param>
        public static async Task<Harness> Start(
            CancellationToken cancellationToken,
            bool withStore = true,
            IReadOnlyList<string>? declared = null
        )
        {
            // A short timeout because the failure this exists for is a hang, not a bad answer: a
            // response that declares chunked framing and writes none leaves the client waiting for
            // a terminator that never comes. The default hundred seconds is a hundred seconds of
            // CI per test.
            var harness = new Harness(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
            var store = new RecordingStore(harness.Stored);

            harness._app = Build(withStore ? store : null);

            harness.Compose(store, declared ?? []);

            await harness._app.StartAsync(cancellationToken);

            harness._client.BaseAddress = new Uri(harness._app.Addresses.First());

            return harness;
        }

        public async Task<string> Get(CancellationToken cancellationToken, string? accept = null)
        {
            var response = await Response(cancellationToken, accept);

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<HttpResponseMessage> Response(
            CancellationToken cancellationToken,
            string? accept = null
        )
        {
            var response = await Raw(cancellationToken, accept);

            response.EnsureSuccessStatusCode();

            return response;
        }

        public Task<HttpResponseMessage> Raw(
            CancellationToken cancellationToken,
            string? accept = null
        )
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/rates");

            if (accept != null)
            {
                request.Headers.TryAddWithoutValidation(KnownHeaders.Accept, accept);
            }

            return _client.SendAsync(request, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();

            await _app.DisposeAsync();
        }

        private static HardenedKestrelApplication Build(IResponseCacheStore? store)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

            new KestrelRuntime().PopulateServiceCollection(services);

            // The filter resolves this from the root provider on its first request, so it has to be
            // in the collection before the application is built.
            if (store != null)
            {
                services.AddSingleton(store);
            }

            // Port 0, so the OS picks one and concurrent test classes cannot collide.
            return HardenedKestrelApplication.Create(
                services,
                kestrel => kestrel.Listen(IPAddress.Loopback, 0)
            );
        }

        /// <summary>
        /// The cache, then the filter that answers. Registered before the server starts, so both
        /// land ahead of the routing filter the runner attaches.
        /// </summary>
        private void Compose(IResponseCacheStore store, IReadOnlyList<string> declared)
        {
            var cache = new ResponseCacheFilter(
                [new EveryRequest()],
                "GET /rates",
                ResponseCacheFilter.DefaultDuration,
                declared: declared
            );

            var middleware = _app.Services.GetRequiredService<IMiddlewareService>();

            middleware.Use(_ => cache);
            middleware.Use(_ => new Answering(this, store, declared));
        }

        /// <summary>One entry for every request, which is what a collection endpoint has.</summary>
        private sealed class EveryRequest : ICacheKeyProvider
        {
            public static ICacheKeyProvider Create(string[] values) => new EveryRequest();

            public ValueTask<string?> Key(IExecutionContext context) => new("only");
        }

        /// <summary>
        /// Writes the answer and stops. Nothing sets <c>ResponseValue</c>, so the response is these
        /// bytes and whatever the host frames them with - which is the point.
        /// </summary>
        private sealed class Answering : IExecutionFilter
        {
            private readonly Harness _harness;
            private readonly IResponseCacheStore _store;
            private readonly IReadOnlyList<string> _declared;

            public Answering(
                Harness harness,
                IResponseCacheStore store,
                IReadOnlyList<string> declared
            )
            {
                _harness = harness;
                _store = store;
                _declared = declared;
            }

            public async Task Execute(IExecutionChain chain)
            {
                var response = chain.Context.Response;

                // What IoFilter does at FilterOrder.Serialization: a request already decided is
                // not bound and its handler is not invoked, so that whatever recorded the failure
                // is what the caller is answered with.
                if (response.ExceptionValue != null)
                {
                    return;
                }

                _harness.Answered++;

                response.Status = 200;
                response.ShouldSerialize = false;

                // What the serializer locator does for an operation that declares what it produces:
                // the client's preferences on the outside, the declared set on the inside. Written
                // out here because this chain has no handler and no serializer.
                var index =
                    _declared.Count == 0
                        ? -1
                        : MediaType.FirstAccepted(chain.Context.Request.Accept, _declared);

                response.ContentType = index < 0 ? "application/json" : _declared[index];

                await response.Body.WriteAsync(
                    Encoding.UTF8.GetBytes(index < 0 ? Answer : "body for " + _declared[index]),
                    chain.Context.CancellationToken
                );
            }
        }

        /// <summary>
        /// An in-process store that keeps what it was handed, so a test can assert on the entry as
        /// well as on the response.
        /// </summary>
        private sealed class RecordingStore : IResponseCacheStore
        {
            private readonly Dictionary<string, CachedResponse> _entries = new(
                StringComparer.Ordinal
            );
            private readonly List<CachedResponse> _stored;

            public RecordingStore(List<CachedResponse> stored)
            {
                _stored = stored;
            }

            public ValueTask<CachedResponse?> Get(
                string key,
                CancellationToken cancellationToken
            ) => new(_entries.GetValueOrDefault(key));

            public ValueTask Set(
                string key,
                CachedResponse response,
                TimeSpan duration,
                CancellationToken cancellationToken
            )
            {
                _entries[key] = response;
                _stored.Add(response);

                return default;
            }

            public ValueTask EvictByTag(string tag, CancellationToken cancellationToken)
            {
                foreach (var entry in _entries.Where(e => e.Value.Tags.Contains(tag)).ToList())
                {
                    _entries.Remove(entry.Key);
                }

                return default;
            }
        }
    }
}
