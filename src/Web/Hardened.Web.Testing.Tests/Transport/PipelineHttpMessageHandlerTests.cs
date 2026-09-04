using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Testing.Tests.Conformance;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>
/// What the handler carries in and out, beyond what the conformance suites hold every transport
/// to: the path decoding measured against Kestrel, the raw body, cancellation, and the base
/// address a client resolves against.
/// </summary>
public class PipelineHttpMessageHandlerTests {

    /// <summary>
    /// The SU-07 probe: the answers Kestrel gives over a socket, which
    /// <c>EncodedPathOverASocketTests</c> in the Kestrel host's own tests measures against the same
    /// table. Here the same escapes reach the handler through an <see cref="HttpClient"/>, which is
    /// what a generated client sends, and arrive decoded the same way.
    /// </summary>
    [Theory]
    [InlineData("/echo/path/%20", "/echo/path/ ")]
    [InlineData("/echo/path/caf%C3%A9", "/echo/path/café")]
    [InlineData("/echo/path/caf%c3%a9", "/echo/path/café")]
    [InlineData("/echo/path/a%5Cb", "/echo/path/a\\b")]
    [InlineData("/echo/path/a%25b", "/echo/path/a%b")]
    [InlineData("/echo/path/a%2Fb", "/echo/path/a%2Fb")]
    [InlineData("/echo/path/a%2fb", "/echo/path/a%2fb")]
    [InlineData("/echo/path/a+b", "/echo/path/a+b")]
    public async Task AnEncodedPathReachesThePipelineTheWayKestrelDecodesIt(string sent, string expected) {
        var host = new PipelineHost();

        using var client = host.Client();
        using var response = await client.GetAsync(sent, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(host.Contexts).Request.Path);
    }

    [Fact]
    public async Task ARelativeUrlResolvesAgainstTheBaseAddressTheHandlerIgnores() {
        var host = new PipelineHost();

        using var client = host.Client();
        using var response = await client.GetAsync("things/42?page=2", TestContext.Current.CancellationToken);

        var request = Assert.Single(host.Contexts).Request;

        Assert.Equal("/things/42", request.Path);
        Assert.Equal("2", request.QueryString.Get("page").ToString());
    }

    [Fact]
    public async Task TheBodyArrivesAsTheBytesTheClientSent() {
        var host = new PipelineHost();
        var malformed = "{\"values\": [1, 2,"u8.ToArray();

        using var client = host.Client();
        using var content = new ByteArrayContent(malformed);

        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        using var response = await client.PostAsync("/registration", content, TestContext.Current.CancellationToken);

        var request = Assert.Single(host.Contexts).Request;

        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);

        Assert.Equal("{\"values\": [1, 2,", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public async Task EveryValueOfAHeaderArrives() {
        var host = new PipelineHost();

        using var client = host.Client();
        using var message = new HttpRequestMessage(HttpMethod.Get, "/");

        message.Headers.TryAddWithoutValidation("X-Many", new[] { "one", "two" });

        using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);

        var headers = Assert.Single(host.Contexts).Request.Headers;

        Assert.Equal(new[] { "one", "two" }, headers["x-many"].ToArray());
    }

    [Fact]
    public async Task TheResponseCarriesStatusHeadersCookiesContentTypeAndBody() {
        var host = new PipelineHost(context => {
            context.Response.Status = 201;
            context.Response.ContentType = "text/plain";
            context.Response.Headers[KnownHeaders.Location] = "/things/7";
            context.Response.Cookies.Append("session", "abc");

            return context.Response.Body.WriteAsync("made"u8.ToArray()).AsTask();
        });

        using var client = host.Client();
        using var response = await client.PostAsync("/things", new StringContent(""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/things/7", response.Headers.Location!.OriginalString);
        Assert.Contains("session=abc", Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("made", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(4, response.Content.Headers.ContentLength);
    }

    /// <summary>
    /// Cancelling the client call cancels the chain: the token the handler reads is the one the
    /// request carried, not a token of the harness's own.
    /// </summary>
    [Fact]
    public async Task ACancelledTokenCancelsTheChain() {
        var reached = new TaskCompletionSource();

        var host = new PipelineHost(async context => {
            reached.SetResult();

            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        });

        using var cancellation = new CancellationTokenSource();
        using var client = host.Client();

        var call = client.GetAsync("/slow", cancellation.Token);

        await reached.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(Assert.Single(host.Contexts).CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task TheCredentialTravelsAsTheTwoTestHeaders() {
        var host = new PipelineHost();

        using var client = host.Client(new TestCredential(new[] { "todos:write", "todos:read" }, "pia"));
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        var headers = Assert.Single(host.Contexts).Request.Headers;

        Assert.Equal("todos:write todos:read", headers["X-Test-Grants"].ToString());
        Assert.Equal("pia", headers["X-Test-Subject"].ToString());
    }

    [Fact]
    public async Task AHeaderTheCallerSetBeatsTheCredential() {
        var host = new PipelineHost();

        using var client = host.Client(new TestCredential(new[] { "todos:write" }));
        using var message = new HttpRequestMessage(HttpMethod.Get, "/");

        message.Headers.TryAddWithoutValidation("X-Test-Grants", "-");

        using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal("-", Assert.Single(host.Contexts).Request.Headers["X-Test-Grants"].ToString());
    }
}
