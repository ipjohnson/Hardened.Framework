using System.Net;
using System.Net.Sockets;
using System.Text;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A plain HEAD against the ASP.NET Core host, on a real socket.
/// </summary>
/// <remarks>
/// <para>
/// <c>HeadRequestTests</c> covers the same route through <c>ITestWebApp</c>, which builds a
/// <c>TestExecutionResponse</c> and so never asks a server to frame anything.
/// <c>HeadRequest.ExecuteWithoutBody</c> swaps <c>Response.Body</c> for a counting stream and puts
/// the original back, and on this host that setter writes through to <c>HttpResponse.Body</c> -
/// which replaces the server's body feature. The trial reported a HEAD tearing the connection down
/// with no status line at all, and nothing here could see it.
/// </para>
/// <para>
/// The 304 half already had a socket test, <c>AHeadHoldingTheTagIs304WithoutALength</c>. This is
/// the 200 half, where <c>Content-Length</c> is actually written.
/// </para>
/// </remarks>
public class HeadOverASocketTests {
    private const string Path = "/binding/path/42";

    /// <summary>A route whose response the conditional filter wraps to tag what it sent.</summary>
    private const string ConditionalPath = "/conditional/generated?culture=en-GB";

    /// <summary>
    /// Read off the socket rather than through <c>HttpClient</c>, which reports a missing status
    /// line as a generic transport failure.
    /// </summary>
    [Fact]
    public async Task AHeadGetsAStatusLine() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var head = await host.Probe("HEAD", Path, TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 200", head);
    }

    [Fact]
    public async Task AHeadCarriesNoBody() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var response = await host.Send(HttpMethod.Head, Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            string.Empty,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// RFC 9110: the HEAD response carries the header fields the GET would have carried.
    /// </summary>
    [Fact]
    public async Task AHeadReportsTheLengthTheGetWouldHaveWritten() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var get = await host.Send(HttpMethod.Get, Path, TestContext.Current.CancellationToken);
        var head = await host.Send(HttpMethod.Head, Path, TestContext.Current.CancellationToken);

        var body = await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body.Length, head.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task AHeadCarriesTheContentTypeOfTheGet() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var get = await host.Send(HttpMethod.Get, Path, TestContext.Current.CancellationToken);
        var head = await host.Send(HttpMethod.Head, Path, TestContext.Current.CancellationToken);

        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
    }

    /// <summary>
    /// The connection survives it. A response whose framing the server could not settle is torn
    /// down, and the next request on the same connection is what notices.
    /// </summary>
    [Fact]
    public async Task TheConnectionIsReusableAfterAHead() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        await host.Send(HttpMethod.Head, Path, TestContext.Current.CancellationToken);

        var next = await host.Send(HttpMethod.Get, Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    #region a conditional route

    /// <summary>
    /// The interaction nothing covered. On <c>/conditional/generated</c> the filter holds the
    /// response back in a <c>ConditionalResponseStream</c> so it can tag the bytes it sent;
    /// <c>HeadRequest</c> puts the original stream back when the chain returns, which drops that
    /// wrapper before its own close-out runs. The 304 half had a socket test and this half did not.
    /// </summary>
    [Fact]
    public async Task AHeadOnAConditionalRouteGetsAStatusLine() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var head = await host.Probe("HEAD", ConditionalPath, TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 200", head);
    }

    [Fact]
    public async Task AHeadOnAConditionalRouteCarriesTheTagTheGetCarried() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var get = await host.Send(HttpMethod.Get, ConditionalPath, TestContext.Current.CancellationToken);
        var head = await host.Send(HttpMethod.Head, ConditionalPath, TestContext.Current.CancellationToken);

        Assert.NotNull(get.Headers.ETag);
        Assert.Equal(get.Headers.ETag, head.Headers.ETag);
    }

    [Fact]
    public async Task TheConnectionIsReusableAfterAHeadOnAConditionalRoute() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        await host.Send(HttpMethod.Head, ConditionalPath, TestContext.Current.CancellationToken);

        var next = await host.Send(HttpMethod.Get, ConditionalPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    #endregion

    private sealed class Host : IAsyncDisposable {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client) {
            _app = app;
            _client = client;
        }

        public static async Task<Host> Start(CancellationToken cancellationToken) {
            var builder = Application.CreateBuilder([]);

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();

            app.UseHardened();

            await app.StartAsync(cancellationToken);

            var client = new HttpClient {
                BaseAddress = new Uri(app.Urls.First()),
                Timeout = TimeSpan.FromSeconds(10)
            };

            return new Host(app, client);
        }

        public Task<HttpResponseMessage> Send(
            HttpMethod method, string path, CancellationToken cancellationToken) =>
            _client.SendAsync(new HttpRequestMessage(method, path), cancellationToken);

        /// <summary>The response head, straight off the socket.</summary>
        public async Task<string> Probe(string method, string path, CancellationToken cancellationToken) {
            var address = new Uri(_app.Urls.First());

            using var socket = new TcpClient();

            await socket.ConnectAsync(address.Host, address.Port, cancellationToken);

            await using var stream = socket.GetStream();

            var request = Encoding.ASCII.GetBytes(
                $"{method} {path} HTTP/1.1\r\nHost: {address.Host}\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(request, cancellationToken);

            using var received = new MemoryStream();

            await stream.CopyToAsync(received, cancellationToken);

            var bytes = received.ToArray();

            Assert.True(bytes.Length > 0, "the server answered nothing at all");

            var separator = bytes.AsSpan().IndexOf("\r\n\r\n"u8);

            Assert.True(separator > 0, "no response headers: " + Encoding.UTF8.GetString(bytes));

            return Encoding.ASCII.GetString(bytes, 0, separator);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
