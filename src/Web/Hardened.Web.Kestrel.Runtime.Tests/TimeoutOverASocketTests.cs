using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// A deadline over a real Kestrel socket: what the filter behind it is handed, and what the filter
/// ahead of it is handed back.
/// </summary>
/// <remarks>
/// <para>
/// The Kestrel host is where <c>FeatureExecutionContext.CancellationToken</c> is seeded from
/// <c>IHttpRequestLifetimeFeature.RequestAborted</c> and is now assignable. Nothing in process
/// exercises that pairing, and the failure it guards against is a write on a dead token - which
/// looks like a hang or a truncated body to the client and like nothing at all to a unit test.
/// </para>
/// <para>
/// The restore is what this exists for. <c>ConditionalGetFilter</c> flushes the body it held back
/// and <c>ResponseCacheFilter</c> copies its buffer to the transport after the inner chain
/// returns, both on <c>context.CancellationToken</c>. Leave the deadline token in place and both
/// of those writes fail on exactly the requests that took longest. <see cref="Flushing"/> is the
/// same shape, reduced to the one line that matters.
/// </para>
/// <para>
/// No routing and no generated handler, as in the conditional and response-cache tests beside
/// this: the chain is the filter and something terminal, which is enough to put the behaviour on
/// the wire.
/// </para>
/// </remarks>
public class TimeoutOverASocketTests {

    private const string Answer = """{"served":true}""";

    /// <summary>
    /// Short enough that a test does not wait on it, long enough not to race a slow CI box.
    /// </summary>
    private const int ShortBudget = 100;

    [Fact]
    public async Task TheHandlerIsCancelledWhenTheBudgetRunsOut() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.True(harness.HandlerWasCancelled);
    }

    /// <summary>
    /// The filter ahead of the deadline writes its answer after the inner chain has come back, on
    /// a token that must be live again. A missing restore is a write on a cancelled token, and
    /// this is what the client would see of it.
    /// </summary>
    [Fact]
    public async Task TheOutwardFlushRunsOnALiveTokenAndReachesTheClient() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        using var response = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Answer, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(harness.FlushFailure);
    }

    /// <summary>
    /// The framing check. The client holds one connection, so the request after the one whose
    /// deadline fired reads whatever that one left behind.
    /// </summary>
    [Fact]
    public async Task TheConnectionIsReusableAfterADeadlineFired() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        using (var first = await harness.Get(TestContext.Current.CancellationToken)) {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await harness.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(Answer, await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A real Kestrel listening on a port the OS picked, with the deadline filter composed as
    /// middleware.
    /// </summary>
    private sealed class Harness : IAsyncDisposable {
        private HardenedKestrelApplication _app = null!;
        private readonly HttpClient _client;

        private Harness(HttpClient client) {
            _client = client;
        }

        /// <summary>Whether the terminal filter's await was cancelled by the budget.</summary>
        public bool HandlerWasCancelled { get; private set; }

        /// <summary>What the outward write failed with, or null. A restore that did not happen.</summary>
        public Exception? FlushFailure { get; private set; }

        public static async Task<Harness> Start(CancellationToken cancellationToken) {
            var harness = new Harness(new HttpClient(new SocketsHttpHandler {
                MaxConnectionsPerServer = 1
            }) { Timeout = TimeSpan.FromSeconds(10) });

            harness._app = Build();

            harness.Compose();

            await harness._app.StartAsync(cancellationToken);

            harness._client.BaseAddress = new Uri(harness._app.Addresses.First());

            return harness;
        }

        public async Task<HttpResponseMessage> Get(CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/rates");

            return await _client.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
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

            // Port 0, so the OS picks one and concurrent test classes cannot collide.
            return HardenedKestrelApplication.Create(
                services, kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
        }

        private void Compose() {
            var middleware = _app.Services.GetRequiredService<IMiddlewareService>();

            middleware.Use(_ => new Flushing(this));
            middleware.Use(_ => new TimeoutFilter(ShortBudget));
            middleware.Use(_ => new Overrunning(this));
        }

        /// <summary>
        /// Writes its answer after the inner chain has returned, on the context's token - the shape
        /// <c>ConditionalGetFilter</c> and <c>ResponseCacheFilter</c> both have.
        /// </summary>
        private sealed class Flushing : IExecutionFilter {
            private readonly Harness _harness;

            public Flushing(Harness harness) {
                _harness = harness;
            }

            public async Task Execute(IExecutionChain chain) {
                var context = chain.Context;

                try {
                    await chain.Next();
                }
                catch (OperationCanceledException) {
                    // The deadline, which the serialization filter would have caught in a routed
                    // application. Held here so the flush below is what the test observes.
                }

                context.Response.Status = 200;
                context.Response.ContentType = "application/json";
                context.Response.ShouldSerialize = false;

                try {
                    await context.Response.Body.WriteAsync(
                        Encoding.UTF8.GetBytes(Answer), context.CancellationToken);
                }
                catch (Exception exception) {
                    _harness.FlushFailure = exception;
                }
            }
        }

        /// <summary>Waits for something that never comes, on whatever token it was handed.</summary>
        private sealed class Overrunning : IExecutionFilter {
            private readonly Harness _harness;

            public Overrunning(Harness harness) {
                _harness = harness;
            }

            public async Task Execute(IExecutionChain chain) {
                try {
                    await Task.Delay(Timeout.Infinite, chain.Context.CancellationToken);
                }
                catch (OperationCanceledException) {
                    _harness.HandlerWasCancelled = true;

                    throw;
                }
            }
        }
    }
}
