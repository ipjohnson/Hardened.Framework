using System.Net;
using System.Net.Sockets;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Testing;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Web.Kestrel.Testing.Tests;

/// <summary>
/// The Kestrel host: what it binds, what it carries in and out, and how it stops.
/// </summary>
public class KestrelTestHostTests {

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task Answer(IExecutionChain chain, int status, string body, string? header = null) {
        var response = chain.Context.Response;

        response.Status = status;
        response.ContentType = "application/json";
        response.ShouldSerialize = false;

        if (header != null) {
            response.Headers["X-Answer"] = header;
        }

        return response.Body.WriteAsync(Encoding.UTF8.GetBytes(body), chain.Context.CancellationToken).AsTask();
    }

    [Fact]
    public async Task StartAsync_BindsALoopbackPortTheKernelPicked() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);

        var address = harness.Host.BaseAddress;

        Assert.Equal("127.0.0.1", address.Host);
        Assert.NotEqual(0, address.Port);
        Assert.Equal("/", address.AbsolutePath);
        Assert.True(harness.Host.IsTerminal);
    }

    [Fact]
    public void BeforeStartThereIsNoAddress() {
        var host = new KestrelTestingAttribute().CreateHost(null!, new Microsoft.Extensions.DependencyInjection.ServiceCollection());

        var failure = Assert.Throws<InvalidOperationException>(() => host.BaseAddress);

        Assert.Contains("has not started", failure.Message);
    }

    /// <summary>The attribute an application names its host with is the one a test names it with.</summary>
    [Fact]
    public void TheProviderAnswersForTheKestrelRuntimeAttribute() {
        Assert.Equal(typeof(KestrelRuntimeAttribute), new KestrelTestingAttribute().RuntimeAttribute);
    }

    /// <summary>
    /// The request as <c>ITestWebApp</c> sends it reaches the chain with its method, its path and
    /// query, its headers and its body, and the answer comes back with its status, every header
    /// Kestrel wrote, and the body as bytes.
    /// </summary>
    [Fact]
    public async Task SendAsync_CarriesTheRequestInAndTheAnswerOut() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 201, "{\"id\":7}", "yes"), Token);

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            ["Content-Type"] = "application/json",
            ["X-Probe"] = "one",
        };
        var body = new MemoryStream("{\"title\":\"t\"}"u8.ToArray());

        var response = await harness.Host.SendAsync(
            new TestHostRequest("POST", "/things?culture=en-GB", headers, body, null), Token);

        var request = Assert.Single(harness.Requests);

        Assert.Equal("POST", request.Method);
        Assert.Equal("/things", request.Path);
        Assert.Equal("one", request.Headers["X-Probe"]);
        Assert.Equal(201, response.StatusCode);
        Assert.Equal("yes", response.Headers["X-Answer"].ToString());
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
        Assert.Null(response.Failure);
        Assert.Equal(7, response.Deserialize<Thing>().Id);
        Assert.Equal(201, LastResponse.Status);
    }

    /// <summary>The body reaches the chain as the bytes the test wrote, never re-serialised.</summary>
    [Fact]
    public async Task SendAsync_CarriesTheBodyBytesAsWritten() {
        string? seen = null;

        await using var harness = await HostHarness.Start(async chain => {
            using var reader = new StreamReader(chain.Context.Request.Body);

            seen = await reader.ReadToEndAsync(chain.Context.CancellationToken);

            await Answer(chain, 200, "\"ok\"");
        }, Token);

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" };

        await harness.Host.SendAsync(
            new TestHostRequest("POST", "/things", headers, new MemoryStream("{\"name\":"u8.ToArray()), null), Token);

        Assert.Equal("{\"name\":", seen);
    }

    /// <summary>A path the chain passes on reaches the real not-found handler behind it.</summary>
    [Fact]
    public async Task AnUnmatchedPathIs404() {
        await using var harness = await HostHarness.Start(chain => chain.Next(), Token);

        var response = await harness.Host.SendAsync(
            new TestHostRequest("GET", "/no/such/route", new Dictionary<string, StringValues>(), Stream.Null, null), Token);

        Assert.Equal(404, response.StatusCode);
    }

    /// <summary>
    /// The credential is on the wire as the two test headers, put there for a request that
    /// carries neither and left alone for one that set its own.
    /// </summary>
    [Fact]
    public async Task CreateHandler_AppliesTheCredentialWhereTheRequestCarriesNone() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);

        using var client = new HttpClient(harness.Host.CreateHandler(new TestCredential(new[] { "pets:read" }))) {
            BaseAddress = harness.Host.BaseAddress
        };

        using var bare = await client.GetAsync("/pets", Token);

        using var own = new HttpRequestMessage(HttpMethod.Get, "/pets");
        own.Headers.Add(TestGrantsPrincipalSource.GrantsHeader, "pets:write");
        using var explicitly = await client.SendAsync(own, Token);

        Assert.Equal("pets:read", harness.Requests[0].Headers[TestGrantsPrincipalSource.GrantsHeader]);
        Assert.Equal("pets:write", harness.Requests[1].Headers[TestGrantsPrincipalSource.GrantsHeader]);
    }

    /// <summary>
    /// What came back over the wire is what <c>LastResponse</c> reports, through a client's own
    /// chain as much as through the harness.
    /// </summary>
    [Fact]
    public async Task AClientBuiltOverTheHostRecordsWhatItReceived() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 204, "", "recorded"), Token);

        using var client = new HttpClient(harness.Host.CreateHandler(null)) { BaseAddress = harness.Host.BaseAddress };

        using var response = await client.DeleteAsync("/things/1", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(204, LastResponse.Status);
        Assert.Equal("recorded", LastResponse.Headers["X-Answer"].ToString());
        Assert.Empty(LastResponse.Body);
    }

    /// <summary>
    /// An event stream never ends, so it is not read to the end for the record: the client gets
    /// the stream as it arrives and <c>LastResponse</c> carries the status and the headers alone.
    /// </summary>
    [Fact]
    public async Task AnEventStreamIsHandedOnWithoutBeingBuffered() {
        await using var harness = await HostHarness.Start(async chain => {
            var response = chain.Context.Response;

            response.Status = 200;
            response.ContentType = "text/event-stream";
            response.ShouldSerialize = false;

            await response.Body.WriteAsync("data: 1\n\n"u8.ToArray(), chain.Context.CancellationToken);
            await response.Body.FlushAsync(chain.Context.CancellationToken);

            // Held open until the client goes away, as a subscription is.
            try {
                await Task.Delay(Timeout.Infinite, chain.Context.CancellationToken);
            }
            catch (OperationCanceledException) {
            }
        }, Token);

        using var client = new HttpClient(harness.Host.CreateHandler(null)) { BaseAddress = harness.Host.BaseAddress };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Token));

        Assert.Equal("data: 1", await reader.ReadLineAsync(Token));
        Assert.Equal("text/event-stream", LastResponse.ContentType);
        Assert.Empty(LastResponse.Body);
    }

    /// <summary>
    /// Disposing the container disposes the host, and the port is closed by the time it returns.
    /// </summary>
    [Fact]
    public async Task DisposingTheContainerClosesThePort() {
        var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);
        var port = harness.Host.BaseAddress.Port;

        await harness.DisposeAsync();

        using var probe = new TcpClient();

        await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port, Token).AsTask());
    }

    /// <summary>
    /// A handler that never completes cannot hold the stop for ever: the client's connection is
    /// closed first, then the server is stopped within the bound, and what is left is aborted.
    /// </summary>
    [Fact]
    public async Task DisposeStopsWithinTheBoundDespiteAHungHandler() {
        var bound = SocketHost.StopBound;

        SocketHost.StopBound = TimeSpan.FromSeconds(1);

        try {
            var harness = await HostHarness.Start(async chain => {
                await new TaskCompletionSource().Task;
            }, Token);

            using var client = new HttpClient(harness.Host.CreateHandler(null)) { BaseAddress = harness.Host.BaseAddress };

            var hung = client.GetAsync("/never", Token);

            // Until the request has reached the handler, or a disposal here would have nothing to wait for.
            while (harness.Requests.Count == 0) {
                await Task.Delay(10, Token);
            }

            var disposal = harness.DisposeAsync().AsTask();
            var finished = await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromSeconds(15), Token));

            Assert.Same(disposal, finished);

            await Assert.ThrowsAnyAsync<Exception>(() => hung);
        }
        finally {
            SocketHost.StopBound = bound;
        }
    }

    private sealed record Thing(int Id);
}
