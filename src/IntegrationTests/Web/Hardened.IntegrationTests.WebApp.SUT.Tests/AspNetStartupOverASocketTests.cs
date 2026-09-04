using System.Net;
using Hardened.Requests.Testing;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The registered startup services, running under the ASP.NET Core host, on a real socket.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseHardened</c> used to install the terminal web handler and nothing else, and authorization
/// arrives as an <c>IStartupService</c>: <c>AuthorizationStartupService</c> is what puts the
/// authorization filter provider into the global filter registry. A host that ran no startup
/// services therefore attached that filter to no handler, and <c>[AuthorizeGrants]</c> answered
/// 200 to a caller presenting nothing. Not a refusal that was too strict - no refusal at all.
/// </para>
/// <para>
/// The scaffold hid it, because the template's <c>Program.cs</c> called
/// <c>ApplicationLogic.Start</c> itself; a hand-written host did not.
/// </para>
/// <para>
/// It has to be a socket test. <c>ITestWebApp</c> runs the same startup loop in its own harness, so
/// the bypass was invisible to every other test in this project.
/// </para>
/// </remarks>
public class AspNetStartupOverASocketTests {

    /// <summary>
    /// The authentication middleware runs, so the caller who presents the grant is recognised as
    /// holding it.
    /// </summary>
    [Fact]
    public async Task ACallerHoldingTheGrantIsAnswered() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var response = await host.Get("/authorization/pets", "pets:read", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"pets\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The half that was answering 200. Without the startup services this route was public.
    /// </summary>
    [Fact]
    public async Task ACallerPresentingNothingIsRefused() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var response = await host.Get("/authorization/pets", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A caller who is known and holds nothing is refused as well, which is the grant check rather
    /// than the absence of a principal.
    /// </summary>
    [Fact]
    public async Task ACallerHoldingNoGrantIsRefused() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var response = await host.Get(
            "/authorization/pets",
            TestGrantsPrincipalSource.AnonymousGrantsValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        public Task<HttpResponseMessage> Get(string path, string? grants, CancellationToken cancellationToken) {
            var request = new HttpRequestMessage(HttpMethod.Get, path);

            if (grants != null) {
                request.Headers.Add(TestGrantsPrincipalSource.GrantsHeader, grants);
            }

            return _client.SendAsync(request, cancellationToken);
        }

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
