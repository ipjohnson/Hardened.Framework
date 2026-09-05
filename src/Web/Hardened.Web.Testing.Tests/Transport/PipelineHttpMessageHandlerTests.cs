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
        var host = new SubstitutePipeline();

        using var client = host.Client();
        using var response = await client.GetAsync(sent, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(host.Contexts).Request.Path);
    }

    /// <summary>
    /// The public constructor, for a test with nothing but a root provider in hand.
    /// </summary>
    [Fact]
    public async Task TheHandlerIsConstructibleFromTheRootProviderAlone() {
        var host = new SubstitutePipeline();

        using var client = new HttpClient(new PipelineHttpMessageHandler(host.Provider)) {
            BaseAddress = new Uri("http://harness/")
        };
        using var response = await client.GetAsync("/plain", TestContext.Current.CancellationToken);

        Assert.Equal("/plain", Assert.Single(host.Contexts).Request.Path);
        Assert.False(Assert.Single(host.Contexts).Request.Headers.ContainsKey("X-Test-Grants"));
    }

    /// <summary>
    /// A message with no base address behind it, which the translation takes as it is: a missing
    /// URI is the root, a relative one is rooted whether or not it was written with a slash.
    /// </summary>
    [Theory]
    [InlineData(null, "/")]
    [InlineData("/things/1?page=2", "/things/1")]
    [InlineData("things/1", "/things/1")]
    public async Task AMessageWithoutABaseAddressIsRootedAsWritten(string? uri, string expectedPath) {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri == null ? null : new Uri(uri, UriKind.Relative));

        var request = await PipelineHttpMessageHandler.CreateRequestAsync(message, null, TestContext.Current.CancellationToken);

        Assert.Equal(expectedPath, request.Path);

        if (uri != null && uri.Contains('?')) {
            Assert.Equal("2", request.QueryString.Get("page").ToString());
        }
    }

    [Fact]
    public async Task ARelativeUrlResolvesAgainstTheBaseAddressTheHandlerIgnores() {
        var host = new SubstitutePipeline();

        using var client = host.Client();
        using var response = await client.GetAsync("things/42?page=2", TestContext.Current.CancellationToken);

        var request = Assert.Single(host.Contexts).Request;

        Assert.Equal("/things/42", request.Path);
        Assert.Equal("2", request.QueryString.Get("page").ToString());
    }

    [Fact]
    public async Task TheBodyArrivesAsTheBytesTheClientSent() {
        var host = new SubstitutePipeline();
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
        var host = new SubstitutePipeline();

        using var client = host.Client();
        using var message = new HttpRequestMessage(HttpMethod.Get, "/");

        message.Headers.TryAddWithoutValidation("X-Many", new[] { "one", "two" });

        using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);

        var headers = Assert.Single(host.Contexts).Request.Headers;

        Assert.Equal(new[] { "one", "two" }, headers["x-many"].ToArray());
    }

    [Fact]
    public async Task TheResponseCarriesStatusHeadersCookiesContentTypeAndBody() {
        var host = new SubstitutePipeline(context => {
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
    /// The content's length is the body's, whatever the pipeline wrote in the header: a length the
    /// bytes contradict would be a message the client refuses to read.
    /// </summary>
    [Fact]
    public async Task AContentLengthThePipelineWroteIsReplacedByTheBodys() {
        var host = new SubstitutePipeline(context => {
            context.Response.Headers[KnownHeaders.ContentLength] = "999";

            return context.Response.Body.WriteAsync("four"u8.ToArray()).AsTask();
        });

        using var client = host.Client();
        using var response = await client.GetAsync("/sized", TestContext.Current.CancellationToken);

        Assert.Equal(4, response.Content.Headers.ContentLength);
        Assert.Equal("four", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Cancelling the client call cancels the chain: the token the handler reads is the one the
    /// request carried, not a token of the harness's own.
    /// </summary>
    [Fact]
    public async Task ACancelledTokenCancelsTheChain() {
        var reached = new TaskCompletionSource();

        var host = new SubstitutePipeline(async context => {
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
        var host = new SubstitutePipeline();

        using var client = host.Client(new TestCredential(new[] { "todos:write", "todos:read" }, "pia"));
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        var headers = Assert.Single(host.Contexts).Request.Headers;

        Assert.Equal("todos:write todos:read", headers["X-Test-Grants"].ToString());
        Assert.Equal("pia", headers["X-Test-Subject"].ToString());
    }

    [Fact]
    public async Task AHeaderTheCallerSetBeatsTheCredential() {
        var host = new SubstitutePipeline();

        using var client = host.Client(new TestCredential(new[] { "todos:write" }));
        using var message = new HttpRequestMessage(HttpMethod.Get, "/");

        message.Headers.TryAddWithoutValidation("X-Test-Grants", "-");

        using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal("-", Assert.Single(host.Contexts).Request.Headers["X-Test-Grants"].ToString());
    }
}
