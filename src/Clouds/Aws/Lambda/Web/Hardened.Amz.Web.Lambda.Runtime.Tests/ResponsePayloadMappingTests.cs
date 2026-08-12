using System.Text;
using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Headers;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// What API Gateway receives, given what the handler wrote.
///
/// <para>
/// The inbound half decides whether a route is found; this half decides whether the caller can use
/// the answer. A status that never leaves the pipeline, a header dropped on the way out or a
/// <c>Set-Cookie</c> that does not parse are all invisible to the function's own logs.
/// </para>
/// </summary>
public class ResponsePayloadMappingTests {

    [Fact]
    public async Task TheStatusTheHandlerSetIsTheStatusReturned() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Status = 418;
            return Task.CompletedTask;
        });

        Assert.Equal(418, response.StatusCode);
    }

    /// <summary>
    /// A handler that never sets a status has succeeded. Returning API Gateway's own default of 0
    /// would surface as a 502 to the caller.
    /// </summary>
    [Fact]
    public async Task AHandlerThatSetsNoStatusReturns200() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event());

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>
    /// Zero is not a status. It is what an <c>int</c> holds before anything sets it, and the
    /// backing field on the proxy response is an <c>int</c>, so "unset" and "zero" are the same
    /// value here.
    /// </summary>
    [Fact]
    public async Task AStatusOfZeroIsTreatedAsUnsetAndReturns200() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Status = 0;
            return Task.CompletedTask;
        });

        Assert.Equal(200, response.StatusCode);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task AnExplicitStatusIsPassedThroughUntouched(int status) {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Status = status;
            return Task.CompletedTask;
        });

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task ResponseHeadersReachTheProxyResponse() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Headers["X-Request-Id"] = "req-9";
            context.Response.ContentType = "application/json";
            return Task.CompletedTask;
        });

        Assert.Equal("req-9", response.Headers["X-Request-Id"]);
        Assert.Equal("application/json", response.Headers[KnownHeaders.ContentType]);
    }

    /// <summary>
    /// A repeated header is one comma-joined value in payload format 2.0, which is what
    /// <c>StringValues.ToString()</c> produces. Before 2026-08-11 the copy used the implicit
    /// <c>StringValues</c> to <c>string</c> conversion, which is nullable (CS8601) and would have
    /// put a JSON null in the header map for a header explicitly set to no value.
    /// </summary>
    [Fact]
    public async Task ARepeatedResponseHeaderIsJoinedRatherThanDropped() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Headers["Vary"] =
                new Microsoft.Extensions.Primitives.StringValues(["Accept", "Origin"]);
            return Task.CompletedTask;
        });

        Assert.Equal("Accept,Origin", response.Headers["Vary"]);
    }

    [Fact]
    public async Task AResponseWithNoHeadersCarriesAnEmptyMapRatherThanNull() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event());

        Assert.NotNull(response.Headers);
        Assert.Empty(response.Headers);
    }

    [Fact]
    public async Task AResponseWithNoCookiesCarriesAnEmptyArrayRatherThanNull() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event());

        Assert.NotNull(response.Cookies);
        Assert.Empty(response.Cookies);
    }

    /// <summary>
    /// A cookie set by a handler comes back in the response's <c>cookies</c> array, which is how
    /// payload format 2.0 carries <c>Set-Cookie</c>.
    ///
    /// <para>
    /// Two defects sat behind this, both fixed 2026-08-11 and both shipped from the first commit.
    /// <c>ApiGatewayV2CookieSetCollection.Append</c> threw <c>NotImplementedException</c>, so no
    /// application hosted on API Gateway could set a cookie at all; and the serialiser appended the
    /// <c>Tuple</c> rather than its value, so had <c>Append</c> worked, every cookie would have
    /// read <c>name=(value, CookieSetOptions { Expires = , ... })</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ACookieSetByTheHandlerComesBackAsNameAndValue() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Cookies.Append(
                "session", "abc123", new CookieSetOptions(Secure: false, HttpOnly: false));
            return Task.CompletedTask;
        });

        Assert.Equal("session=abc123", Assert.Single(response.Cookies));
    }

    [Fact]
    public async Task ACookiesAttributesAreAppendedAfterTheValue() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Cookies.Append("session", "abc123", new CookieSetOptions(
                Path: "/", Domain: "example.com", SameSite: SameSite.Lax));
            return Task.CompletedTask;
        });

        Assert.Equal(
            "session=abc123; Domain=example.com; Path=/; SameSite=Lax; HttpOnly; Secure",
            Assert.Single(response.Cookies));
    }

    /// <summary>
    /// The serialiser reuses one pooled builder across cookies, so a missing <c>Clear</c> would
    /// leave the second cookie carrying the first.
    /// </summary>
    [Fact]
    public async Task EachCookieIsSerialisedIndependentlyOfTheOneBeforeIt() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            var options = new CookieSetOptions(Secure: false, HttpOnly: false);
            context.Response.Cookies.Append("session", "abc123", options);
            context.Response.Cookies.Append("theme", "dark", options);
            return Task.CompletedTask;
        });

        Assert.Equal(["session=abc123", "theme=dark"], response.Cookies);
    }

    [Fact]
    public async Task ATextBodyIsReturnedAsUtf8Text() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            var bytes = Encoding.UTF8.GetBytes("""{"ok":true}""");
            context.Response.Body.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        });

        Assert.Equal("""{"ok":true}""", response.Body);
        Assert.False(response.IsBase64Encoded);
    }

    /// <summary>
    /// Bytes that are not valid UTF-8 have to travel as base64, and API Gateway has to be told so
    /// with <c>isBase64Encoded</c>. Sending them as text substitutes replacement characters and
    /// corrupts the payload with a 200 status.
    /// </summary>
    [Fact]
    public async Task ABinaryBodyIsBase64EncodedAndFlagged() {
        var harness = new ApiGatewayHarness();
        var payload = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF };

        var response = await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.IsBinary = true;
            context.Response.Body.Write(payload, 0, payload.Length);
            return Task.CompletedTask;
        });

        Assert.True(response.IsBase64Encoded);
        Assert.Equal(payload, Convert.FromBase64String(response.Body));
    }

    [Fact]
    public async Task AHandlerThatWritesNothingReturnsAnEmptyBody() {
        var harness = new ApiGatewayHarness();

        var response = await harness.Process(ApiGatewayHarness.Event());

        Assert.Equal("", response.Body);
        Assert.False(response.IsBase64Encoded);
    }

    /// <summary>
    /// The response buffer is leased from a pool and returned at the end of the invocation. A lease
    /// handed out without being reset is how a warm function answers one request with the body of
    /// the one before it.
    /// </summary>
    [Fact]
    public async Task ASecondInvocationDoesNotSeeTheFirstResponseBody() {
        var harness = new ApiGatewayHarness();

        var first = await harness.Process(ApiGatewayHarness.Event(), context => {
            var bytes = Encoding.UTF8.GetBytes("first response");
            context.Response.Body.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        });

        var second = await harness.Process(ApiGatewayHarness.Event(), context => {
            var bytes = Encoding.UTF8.GetBytes("2nd");
            context.Response.Body.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        });

        Assert.Equal("first response", first.Body);
        Assert.Equal("2nd", second.Body);
    }

    /// <summary>
    /// A response only counts as started once something has been written to it, which is what tells
    /// an error filter whether it can still replace the body with a problem document.
    /// </summary>
    [Fact]
    public async Task AResponseIsNotStartedUntilSomethingIsWritten() {
        var harness = new ApiGatewayHarness();
        var beforeWrite = true;
        var afterWrite = false;

        await harness.Process(ApiGatewayHarness.Event(), context => {
            beforeWrite = context.Response.ResponseStarted;
            context.Response.Body.WriteByte((byte)'x');
            afterWrite = context.Response.ResponseStarted;
            return Task.CompletedTask;
        });

        Assert.False(beforeWrite);
        Assert.True(afterWrite);
    }
}
