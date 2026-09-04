using System.Net;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A 304 through the ASP.NET Core host, on a real socket.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>ConditionalGetOverASocketTests</c> in the Kestrel runtime's tests, for the
/// reason the response-cache pair exists: every other test in this project runs through
/// <c>ITestWebApp</c>, which builds a <c>TestExecutionResponse</c>, so the host this application
/// declares never answers. A 304 is a status decided on the first body write and a body that
/// must then not be written, and both of those are the host's to get right.
/// </para>
/// <para>
/// It reads <c>/response-cache/catalog</c>, which is cached and unauthenticated, so the validator
/// on the wire is the one the cache wrote.
/// </para>
/// </remarks>
public class ConditionalGetOverASocketTests {

    private const string FirstAnswer = "\"en-GB-1\"";

    [Fact]
    public async Task AClientHoldingTheTagIsAnswered304WithNoBody() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var miss = await host.Get(TestContext.Current.CancellationToken);
        var tag = Assert.IsType<System.Net.Http.Headers.EntityTagHeaderValue>(miss.Headers.ETag);

        using var revalidated = await host.Revalidate(tag.ToString(), HttpMethod.Get, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        AssertNoContentHeaders(revalidated);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(tag, revalidated.Headers.ETag);
    }

    /// <summary>
    /// The framing check. The client holds one connection, so the request after the 304 reads
    /// whatever the 304 left behind; getting the whole stored body, with the counter still at 1,
    /// means it left nothing.
    /// </summary>
    [Fact]
    public async Task TheConnectionIsReusableAfterA304() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var miss = await host.Get(TestContext.Current.CancellationToken);

        using (var revalidated = await host.Revalidate(
                   miss.Headers.ETag!.ToString(), HttpMethod.Get, TestContext.Current.CancellationToken)) {
            Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        }

        var hit = await host.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        Assert.Equal(FirstAnswer, await hit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AHeadHoldingTheTagIs304WithoutALength() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var miss = await host.Get(TestContext.Current.CancellationToken);

        using var head = await host.Revalidate(
            miss.Headers.ETag!.ToString(), HttpMethod.Head, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, head.StatusCode);
        AssertNoContentHeaders(head);
    }

    /// <summary>
    /// Asked of the headers as they arrived, before the body is read. Reading it buffers the
    /// content, and <c>HttpClient</c> then records the buffer's length in this collection as
    /// though a <c>Content-Length</c> had been on the wire.
    /// </summary>
    private static void AssertNoContentHeaders(HttpResponseMessage response) {
        Assert.False(response.Content.Headers.Contains("Content-Length"));
        Assert.False(response.Content.Headers.Contains("Content-Type"));
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
        public async Task<HttpResponseMessage> Get(CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/response-cache/catalog?culture=en-GB");

            return await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        /// <summary>
        /// A request holding <paramref name="ifNoneMatch"/>, returned as soon as the headers are
        /// in, so a test can see them as they arrived. The caller disposes it.
        /// </summary>
        public async Task<HttpResponseMessage> Revalidate(
            string ifNoneMatch, HttpMethod method, CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(method, "/response-cache/catalog?culture=en-GB");

            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
