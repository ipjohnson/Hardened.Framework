using System.Net;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A 504 on a real socket, through the ASP.NET Core host.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of the conditional-GET and response-cache pairs beside this one, and written for
/// the reason the 0.19 trial kept supplying: every other test in this project runs through
/// <c>ITestWebApp</c>, which builds a <c>TestExecutionResponse</c> in memory, so a status that is
/// right there can still be wrong on the wire. A deadline is exactly the case where that matters -
/// it fires while the host is mid-request, and whether the status and the framing survive it is
/// the host's to get right.
/// </para>
/// <para>
/// It also covers the one host-specific part of the change.
/// <c>AspNetExecutionContext.CancellationToken</c> is computed from
/// <c>HttpContext.RequestAborted</c> on every read rather than captured, because ASP.NET
/// middleware may replace the lifetime feature mid-request; it now falls through to that until a
/// filter assigns one. Nothing in process exercises that path.
/// </para>
/// </remarks>
public class TimeoutOverASocketTests {

    [Fact]
    public async Task AHandlerThatOutlivesItsBudgetAnswers504OnTheWire() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        using var response = await host.Get("/timeout/slow", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains(
            "GatewayTimeout",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADeclaredShedStatusAndItsRetryAfterArriveOnTheWire() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        using var response = await host.Get("/timeout/shed", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            TimeSpan.FromSeconds(30), response.Headers.RetryAfter!.Delta);
    }

    /// <summary>
    /// The framing check. The client holds one connection, so the request after the 504 reads
    /// whatever the 504 left behind - and a deadline that fired while the host was mid-response is
    /// the way to leave something behind.
    /// </summary>
    [Fact]
    public async Task TheConnectionIsReusableAfterA504() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        using (var timedOut = await host.Get("/timeout/slow", TestContext.Current.CancellationToken)) {
            Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
        }

        using var next = await host.Get("/timeout/unbounded", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(
            "\"unbounded-1\"",
            await next.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

            // Port 0, so the OS picks one and nothing collides with a parallel test class.
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();

            app.UseHardened();

            await app.StartAsync(cancellationToken);

            // One connection, so every request is framed by the one before it, and a short
            // timeout because the failure this exists for is a hang.
            var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1 }) {
                BaseAddress = new Uri(app.Urls.First()),
                Timeout = TimeSpan.FromSeconds(10)
            };

            return new Host(app, client);
        }

        /// <summary>A full answer, read to the end so the connection is free for the next.</summary>
        public async Task<HttpResponseMessage> Get(string path, CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);

            return await _client.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
