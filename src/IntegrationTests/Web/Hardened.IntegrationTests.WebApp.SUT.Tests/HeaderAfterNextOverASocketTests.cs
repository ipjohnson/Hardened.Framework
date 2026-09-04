using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A filter that writes a response header after <c>Next()</c>, on a real socket, under the ASP.NET
/// Core host.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET's header dictionary is read-only once the response has started, and its setter throws.
/// The Kestrel host catches that, records it through the application's <c>IRequestLogger</c> and
/// finishes the response; the ASP.NET host let it escape to the server, which aborts the
/// connection - so the caller got no answer at all rather than the answer the handler had already
/// written.
/// </para>
/// <para>
/// Writing headers on the way out is still the wrong shape - <c>CorrelationHeaderFilter</c> says
/// why, and sets its own on the way in. This is about what the framework does when an application
/// gets it wrong, and about the two hosts doing the same thing.
/// </para>
/// </remarks>
public class HeaderAfterNextOverASocketTests {

    [Fact]
    public async Task TheAnswerTheHandlerWroteStillReachesTheCaller() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        var response = await host.Get(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "\"unguarded\"",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The filter really did throw, so the test above is the framework absorbing it rather than the
    /// write having quietly succeeded.
    /// </summary>
    [Fact]
    public async Task TheHeaderWriteFails() {
        await using var host = await Host.Start(TestContext.Current.CancellationToken);

        await host.Get(TestContext.Current.CancellationToken);

        Assert.NotNull(host.Filter.Failure);
    }

    /// <summary>
    /// A filter appended to the middleware chain that writes a response header once the chain
    /// beneath it has finished.
    /// </summary>
    private sealed class HeaderAfterNextFilter : IExecutionFilter {
        public Exception? Failure { get; private set; }

        public async Task Execute(IExecutionChain chain) {
            await chain.Next();

            try {
                chain.Context.Response.Headers["X-Written-After-Next"] = "yes";
            }
            catch (Exception exception) {
                Failure = exception;

                throw;
            }
        }
    }

    private sealed class Host : IAsyncDisposable {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client, HeaderAfterNextFilter filter) {
            _app = app;
            _client = client;
            Filter = filter;
        }

        public HeaderAfterNextFilter Filter { get; }

        public static async Task<Host> Start(CancellationToken cancellationToken) {
            var builder = Application.CreateBuilder([]);

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();

            var filter = new HeaderAfterNextFilter();

            // Appended before UseHardened, so it sits ahead of the terminal web handler and gets
            // its turn on the way back out.
            app.Services.GetRequiredService<IMiddlewareService>().Use(_ => filter);

            app.UseHardened();

            await app.StartAsync(cancellationToken);

            // Short, because the failure this exists for is an aborted connection.
            var client = new HttpClient {
                BaseAddress = new Uri(app.Urls.First()),
                Timeout = TimeSpan.FromSeconds(10)
            };

            return new Host(app, client, filter);
        }

        public Task<HttpResponseMessage> Get(CancellationToken cancellationToken) =>
            _client.GetAsync("/authorization/unguarded", cancellationToken);

        public async ValueTask DisposeAsync() {
            _client.Dispose();

            await _app.DisposeAsync();
        }
    }
}
