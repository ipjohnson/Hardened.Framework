using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// A streamed response over a real Kestrel socket: server-sent events and newline-delimited JSON,
/// through the filter that writes them and the body the host actually hands it.
/// </summary>
/// <remarks>
/// <para>
/// Every streaming test in the repository ran through <c>ITestWebApp</c> or the filter tests,
/// both of which write into a <c>MemoryStream</c>. Kestrel's response body refuses a synchronous
/// write unless <c>AllowSynchronousIO</c> is turned on, and the framings wrote their prefixes and
/// newlines synchronously - so every event stream answered 500 with an empty body on both hosts,
/// and the first newline-delimited item ended the stream, while the suite was green. This is the
/// test that would have said so.
/// </para>
/// <para>
/// No routing and no generated handler, as in the other socket tests beside this: the chain is
/// the IO filter and something terminal that hands it a sequence, which is enough to put the
/// framing on the wire.
/// </para>
/// </remarks>
public class StreamingOverASocketTests {

    [Fact]
    public async Task AnEventStreamReachesTheClientWithItsFraming() {
        await using var harness = await Harness.Start(SseFraming.Instance, TestContext.Current.CancellationToken);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "id: 1\ndata: alpha\n\ndata: beta\n\n",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The completion of an empty event stream is the one write that asked the body where it was,
    /// and Kestrel's body does not say: the comment went unwritten and the request logged a fault
    /// after answering 200.
    /// </summary>
    [Fact]
    public async Task AnEmptyEventStreamEndsWithItsCommentAndNoFault() {
        await using var harness = await Harness.Start(SseFraming.Instance, TestContext.Current.CancellationToken, empty: true);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(":\n\n", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(harness.Failures);
    }

    /// <summary>And a stream that produced events does not get the comment, nor a fault.</summary>
    [Fact]
    public async Task AnEventStreamThatProducedEventsLogsNoFault() {
        await using var harness = await Harness.Start(SseFraming.Instance, TestContext.Current.CancellationToken);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(":\n\n", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public async Task ANewlineDelimitedStreamReachesTheClientWhole() {
        await using var harness = await Harness.Start(NdjsonFraming.Instance, TestContext.Current.CancellationToken);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "alpha\nbeta\n\n",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A real Kestrel listening on a port the OS picked, with the streaming IO filter composed as
    /// middleware over a filter that produces the sequence. <c>AllowSynchronousIO</c> is left at
    /// its default, which is the whole point.
    /// </summary>
    private sealed class Harness : IAsyncDisposable {
        private HardenedKestrelApplication _app = null!;
        private readonly HttpClient _client;

        private Harness(HttpClient client) {
            _client = client;
        }

        /// <summary>What the request logger was told failed, which a 200 with a full body hides.</summary>
        public List<Exception> Failures { get; } = [];

        public static async Task<Harness> Start(
            IStreamFraming framing, CancellationToken cancellationToken, bool empty = false) {
            var harness = new Harness(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

            harness._app = Build(harness);

            harness.Compose(framing, empty);

            await harness._app.StartAsync(cancellationToken);

            harness._client.BaseAddress = new Uri(harness._app.Addresses.First());

            return harness;
        }

        public async Task<HttpResponseMessage> Get(CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/feed");

            return await _client.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }

        private static HardenedKestrelApplication Build(Harness harness) {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

            new KestrelRuntime().PopulateServiceCollection(services);

            // Ahead of the runtime's own, which registers with Try. A fault after the response
            // started is invisible to the client, so the logger is where it shows.
            services.AddSingleton<IRequestLogger>(new Recording(harness.Failures));

            // Port 0, so the OS picks one and concurrent test classes cannot collide.
            return HardenedKestrelApplication.Create(
                services, kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        }

        private void Compose(IStreamFraming framing, bool empty) {
            var middleware = _app.Services.GetRequiredService<IMiddlewareService>();

            middleware.Use(_ => new AsyncEnumerableIoFilter<object>(
                _ => Task.FromResult<IExecutionRequestParameters>(EmptyParameters.Instance),
                WriteValue,
                null,
                framing));
            middleware.Use(_ => new Producing(ReferenceEquals(framing, SseFraming.Instance), empty));
        }

        /// <summary>Keeps what the pipeline reported as failed, and nothing else.</summary>
        private sealed class Recording : IRequestLogger {
            private readonly List<Exception> _failures;

            public Recording(List<Exception> failures) {
                _failures = failures;
            }

            public void RequestBegin(IExecutionContext context) { }

            public void RequestMapped(IExecutionContext context) { }

            public void RequestEnd(IExecutionContext context) { }

            public void RequestParameterBindFailed(IExecutionContext context, Exception? exp) {
                if (exp != null) {
                    _failures.Add(exp);
                }
            }

            public void RequestFailed(IExecutionContext context, Exception exp) => _failures.Add(exp);

            public void ResourceNotFound(IExecutionContext context) { }
        }

        /// <summary>
        /// Stands in for the serializer by writing the item's text, asynchronously, so what the
        /// test measures is the framing around it.
        /// </summary>
        private static Task WriteValue(IExecutionContext context) {
            var bytes = Encoding.UTF8.GetBytes(context.Response.ResponseValue?.ToString() ?? "");

            return context.Response.Body.WriteAsync(bytes, 0, bytes.Length, context.CancellationToken);
        }

        /// <summary>
        /// The terminal filter: hands the IO filter a sequence. For the event stream the first item
        /// carries an id, so the field line is on the wire too; the newline-delimited framing is
        /// handed plain items, since it frames whatever it is given as it is.
        /// </summary>
        private sealed class Producing : IExecutionFilter {
            private readonly bool _events;
            private readonly bool _empty;

            public Producing(bool events, bool empty) {
                _events = events;
                _empty = empty;
            }

            public Task Execute(IExecutionChain chain) {
                chain.Context.Response.ResponseValue = _empty ? Nothing() : Items(_events);

                return Task.CompletedTask;
            }

            private static async IAsyncEnumerable<object> Nothing() {
                await Task.Yield();

                yield break;
            }

            private static async IAsyncEnumerable<object> Items(bool events) {
                yield return events ? new SseItem<string>("alpha", Id: "1") : "alpha";

                await Task.Yield();

                yield return "beta";
            }
        }
    }
}
