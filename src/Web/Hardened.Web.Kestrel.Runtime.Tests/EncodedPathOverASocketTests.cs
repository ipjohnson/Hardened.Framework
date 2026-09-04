using System.Net;
using System.Net.Sockets;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// What Kestrel hands the pipeline for a percent-encoded path, measured over a real socket.
/// </summary>
/// <remarks>
/// <para>
/// The SU-07 probe from the 0.18 trial, kept as a test. <c>RequestPathDecoder</c> in
/// <c>Hardened.Web.Testing</c> documents this table and decodes the harness's paths by it, and
/// <c>PipelineHttpMessageHandlerTests</c> holds the in-process transport to the same rows; this is
/// the half that says the table is still Kestrel's. A row here that moves is a Kestrel change the
/// harness has to follow.
/// </para>
/// <para>
/// A raw socket rather than an <see cref="HttpClient"/>, because <see cref="Uri"/> will not carry
/// every row - <c>a%zz</c> is not a URI - and the point is what the server does with the bytes a
/// client actually sends. In the shape <c>ResponseCacheOverASocketTests</c> added: no routing, one
/// terminal filter that echoes the path it was given.
/// </para>
/// </remarks>
public class EncodedPathOverASocketTests {

    [Theory]
    [InlineData("/echo/path/%20", "/echo/path/ ")]
    [InlineData("/echo/path/caf%C3%A9", "/echo/path/café")]
    [InlineData("/echo/path/caf%c3%a9", "/echo/path/café")]
    [InlineData("/echo/path/a%5Cb", "/echo/path/a\\b")]
    [InlineData("/echo/path/a%25b", "/echo/path/a%b")]
    [InlineData("/echo/path/a%2Fb", "/echo/path/a%2Fb")]
    [InlineData("/echo/path/a%2fb", "/echo/path/a%2fb")]
    [InlineData("/echo/path/a+b", "/echo/path/a+b")]
    [InlineData("/echo/path/a%zz", "/echo/path/a%zz")]
    [InlineData("/echo/path/a%", "/echo/path/a%")]
    [InlineData("/echo/path/a%2", "/echo/path/a%2")]
    public async Task KestrelDecodesThePathByTheTableTheHarnessUses(string sent, string expected) {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        Assert.Equal(expected, await harness.Probe(sent, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Kestrel with one filter that answers the request path as the body, on a port the OS picked.
    /// </summary>
    private sealed class Harness : IAsyncDisposable {
        private readonly HardenedKestrelApplication _app;

        private Harness(HardenedKestrelApplication app) {
            _app = app;
        }

        public static async Task<Harness> Start(CancellationToken cancellationToken) {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

            new KestrelRuntime().PopulateServiceCollection(services);

            var app = HardenedKestrelApplication.Create(services, kestrel => kestrel.Listen(IPAddress.Loopback, 0));

            app.Services.GetRequiredService<IMiddlewareService>().Use(_ => new EchoPath());

            await app.StartAsync(cancellationToken);

            return new Harness(app);
        }

        /// <summary>
        /// One request line, written as-is, and the body of the answer.
        /// </summary>
        public async Task<string> Probe(string path, CancellationToken cancellationToken) {
            var address = new Uri(_app.Addresses.First());

            using var socket = new TcpClient();

            await socket.ConnectAsync(address.Host, address.Port, cancellationToken);

            await using var stream = socket.GetStream();

            var request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {address.Host}\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(request, cancellationToken);

            using var received = new MemoryStream();

            await stream.CopyToAsync(received, cancellationToken);

            var bytes = received.ToArray();
            var separator = IndexOf(bytes, "\r\n\r\n"u8, 0);

            Assert.True(separator > 0, "no response headers: " + Encoding.UTF8.GetString(bytes));

            var head = Encoding.ASCII.GetString(bytes, 0, separator);

            Assert.StartsWith("HTTP/1.1 200", head);

            // Chunked, since the filter declares no length: one chunk, its size line, then the
            // terminator. The size is in bytes, which is why this works on bytes rather than on
            // the decoded text - a multi-byte character is one character and more than one byte.
            var bodyStart = separator + 4;

            if (!head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)) {
                return Encoding.UTF8.GetString(bytes, bodyStart, bytes.Length - bodyStart);
            }

            var sizeEnd = IndexOf(bytes, "\r\n"u8, bodyStart);
            var size = Convert.ToInt32(Encoding.ASCII.GetString(bytes, bodyStart, sizeEnd - bodyStart), 16);

            return Encoding.UTF8.GetString(bytes, sizeEnd + 2, size);
        }

        private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int start) {
            var index = haystack.AsSpan(start).IndexOf(needle);

            return index < 0 ? -1 : start + index;
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();

        private sealed class EchoPath : IExecutionFilter {
            public async Task Execute(IExecutionChain chain) {
                var response = chain.Context.Response;

                response.Status = 200;
                response.ContentType = "text/plain; charset=utf-8";
                response.ShouldSerialize = false;

                await response.Body.WriteAsync(
                    Encoding.UTF8.GetBytes(chain.Context.Request.Path), chain.Context.CancellationToken);
            }
        }
    }
}
