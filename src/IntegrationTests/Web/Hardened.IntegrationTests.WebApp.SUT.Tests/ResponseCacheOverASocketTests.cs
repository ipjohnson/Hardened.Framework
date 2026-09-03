using System.Net;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A response-cache hit through the ASP.NET Core host, on a real socket.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>ResponseCacheOverASocketTests</c> in the Kestrel runtime's tests, and the
/// half that was missing. Both hosts answered a cache hit incorrectly at 0.19.0-rc1000 - Kestrel
/// with an empty body, ASP.NET Core with <c>chunk hex-length char not a hex digit</c> - and
/// neither could be seen from here, because every other test in this project is driven through
/// <c>ITestWebApp</c>. That harness builds a <c>TestExecutionResponse</c>, so the host this
/// application declares is never the one answering: <c>[AspNetCoreRuntime]</c> decides what is
/// registered and nothing more.
/// </para>
/// <para>
/// So this one starts the application the way <c>Program.cs</c> does and asks for the bytes over
/// TCP. It uses <c>/response-cache/catalog</c>, which is cached and unauthenticated, because
/// authentication does not run under the ASP.NET host with the startup the template writes - that
/// is a separate open defect, and it is not what this test is measuring.
/// </para>
/// </remarks>
public class ResponseCacheOverASocketTests {

    /// <summary>
    /// What the handler answers on its first request. A hit has to carry the same bytes; the
    /// counter in the body is what proves the handler did not run again.
    /// </summary>
    private const string FirstAnswer = "\"en-GB-1\"";

    /// <summary>
    /// The framing defect. The entry captured the host's own <c>Transfer-Encoding: chunked</c> and
    /// the hit re-declared it, then wrote the stored bytes with no chunk header and no terminator.
    /// </summary>
    [Fact]
    public async Task AHitCarriesTheWholeBodyTheMissCarried() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var miss = await host.Get(TestContext.Current.CancellationToken);
        var hit = await host.Get(TestContext.Current.CancellationToken);

        Assert.Equal(FirstAnswer, miss);
        Assert.Equal(FirstAnswer, hit);
    }

    /// <summary>
    /// And the second answer was the store's, not a second run of the handler.
    /// </summary>
    [Fact]
    public async Task AHitIsAnsweredWithoutRunningTheHandler() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        await host.Get(TestContext.Current.CancellationToken);

        var repeat = await host.Get(TestContext.Current.CancellationToken);

        // The counter only advances when the handler runs, so a second "-1" is the cache
        // answering and a "-2" is the handler having run twice.
        Assert.Equal(FirstAnswer, repeat);
    }

    /// <summary>
    /// A hit carries the id of the request it answered rather than the one that filled the entry.
    /// </summary>
    /// <remarks>
    /// <see cref="CorrelationHeaderFilter"/> is seeded into every host's middleware chain and runs
    /// ahead of the cache, so the current request's id is on the response before a hit is
    /// replayed. Storing it handed one caller's id to everyone else for the whole duration.
    /// </remarks>
    [Fact]
    public async Task AHitCarriesTheCallersOwnCorrelationId() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var miss = await host.Response(TestContext.Current.CancellationToken);
        var hit = await host.Response(TestContext.Current.CancellationToken);

        var first = miss.Headers.GetValues(CorrelationHeaderFilter.HeaderName).Single();
        var second = hit.Headers.GetValues(CorrelationHeaderFilter.HeaderName).Single();

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The application, started as <c>Program.cs</c> starts it, listening on a port the OS picked.
    /// </summary>
    private sealed class Host : IAsyncDisposable {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client) {
            _app = app;
            _client = client;
        }

        public static async Task<Host> Start(CancellationToken cancellationToken) {
            var builder = Application.CreateBuilder([]);

            // Port 0, so the OS picks one and nothing collides with a parallel test class or with
            // whatever else is listening on this machine.
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();

            app.UseHardened();

            await app.StartAsync(cancellationToken);

            // A short timeout because the failure this exists for is a hang: a response that
            // declares chunked framing and writes none leaves the client waiting for a terminator
            // that never arrives.
            var client = new HttpClient {
                BaseAddress = new Uri(app.Urls.First()),
                Timeout = TimeSpan.FromSeconds(10)
            };

            return new Host(app, client);
        }

        public async Task<string> Get(CancellationToken cancellationToken) {
            var response = await Response(cancellationToken);

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<HttpResponseMessage> Response(CancellationToken cancellationToken) {
            var response = await _client.GetAsync(
                "/response-cache/catalog?culture=en-GB", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return response;
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
