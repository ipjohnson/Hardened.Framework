using System.IO.Compression;
using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Compression;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// The response compression filter over a real Kestrel socket.
///
/// <para>
/// The in-memory test client cannot see what matters here: that the headers the filter writes on
/// the first body write are still changeable when Kestrel sends them, that the encoder's trailer
/// reaches the wire before the host completes the response, and that a body whose announced
/// length was dropped is framed by the host rather than cut off.
/// </para>
/// </summary>
public class CompressionOverASocketTests {

    private static readonly string Answer =
        "{\"readings\":[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"{{\"sensor\":\"s{i}\",\"value\":{i}}}")) + "]}";

    [Fact]
    public async Task AClientAcceptingGzipGetsOneMemberThatDecodesToTheAnswer() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var response = await harness.Get("gzip, deflate, br", TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.True(bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b);
        Assert.True(bytes.Length < Answer.Length);
        Assert.Equal(Answer, Decode(bytes));
    }

    [Fact]
    public async Task AClientAcceptingNothingGetsThePlainBody() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var response = await harness.Get(null, TestContext.Current.CancellationToken);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(Answer, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The handler announced the identity length. The filter drops it, so whatever length Kestrel
    /// sends is one it measured itself, on the compressed bytes. Kestrel writes one for a body it
    /// held whole before the response started, which is this one.
    /// </summary>
    [Fact]
    public async Task ACompressedResponseIsFramedByTheHostAndVariesOnAcceptEncoding() {
        await using var harness = await Harness.Start(TestContext.Current.CancellationToken);

        var response = await harness.Get("gzip", TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(Answer.Length, bytes.Length);
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength ?? bytes.Length);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    private static string Decode(byte[] bytes) {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private sealed class Harness : IAsyncDisposable {
        private readonly HttpClient _client;

        private HardenedKestrelApplication _app = null!;

        private Harness(HttpClient client) {
            _client = client;
        }

        public static async Task<Harness> Start(CancellationToken cancellationToken) {
            // No automatic decompression, so the coding header and the bytes arrive as sent.
            var harness = new Harness(new HttpClient(new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.None
            }) { Timeout = TimeSpan.FromSeconds(10) });

            harness._app = Build();

            harness.Compose();

            await harness._app.StartAsync(cancellationToken);

            harness._client.BaseAddress = new Uri(harness._app.Addresses.First());

            return harness;
        }

        public async Task<HttpResponseMessage> Get(string? acceptEncoding, CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/readings");

            if (acceptEncoding != null) {
                request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            }

            var response = await _client.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            return response;
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
                services, kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        }

        private void Compose() {
            var middleware = _app.Services.GetRequiredService<IMiddlewareService>();

            middleware.Use(_ => new ResponseCompressionFilter(configuration: new CompressionConfiguration()));
            middleware.Use(_ => new Answering());
        }

        private sealed class Answering : IExecutionFilter {
            public async Task Execute(IExecutionChain chain) {
                var response = chain.Context.Response;
                var bytes = Encoding.UTF8.GetBytes(Answer);

                response.Status = 200;
                response.ContentType = "application/json";
                response.Headers["Content-Length"] = bytes.Length.ToString();
                response.ShouldSerialize = false;

                await response.Body.WriteAsync(bytes, chain.Context.CancellationToken);
            }
        }
    }
}
