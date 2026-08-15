using Hardened.Requests.Abstract.Headers;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// The status contract, which is subtler than it looks and was wrong for a long time.
///
/// <c>ResourceNotFoundHandler</c> supplies a 404 only when it finds the status still unset, so the
/// getter has to distinguish "nothing has decided yet" from "200". Reading
/// <c>HttpResponse.StatusCode</c> back — which ASP.NET initialises to 200 — collapses that
/// distinction and stops the handler ever firing.
/// </summary>
public class AspNetExecutionResponseTests {

    [Fact]
    public void Status_IsNullBeforeAnythingSetsIt() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response);

        Assert.Null(response.Status);
    }

    [Fact]
    public void Status_WritesThroughToTheHttpResponse() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Status = 404;

        Assert.Equal(404, response.Status);
        Assert.Equal(404, httpContext.Response.StatusCode);
    }

    /// <summary>Clearing the status returns the response to the default rather than to zero.</summary>
    [Fact]
    public void Status_NullResetsTheHttpResponseToTwoHundred() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response) { Status = 500 };

        response.Status = null;

        Assert.Null(response.Status);
        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// Once the response has started the status is settled, so it is reported rather than left
    /// null. Without this every successful request logs a blank status at <c>RequestEnd</c>,
    /// because nothing sets a status on an ordinary success path.
    /// </summary>
    [Fact]
    public void Status_ReportsTheResponseStatusOnceTheResponseHasStarted() {
        var httpContext = StartableResponseContext.Create(
            Substitute.For<IServiceProvider>(), out var start);
        var response = new AspNetExecutionResponse(httpContext.Response);

        Assert.Null(response.Status);

        httpContext.Response.StatusCode = 201;
        start();

        Assert.Equal(201, response.Status);
    }

    [Fact]
    public void Clone_CarriesTheStatus() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response) { Status = 418 };

        var clone = response.Clone(null);

        Assert.Equal(418, clone.Status);
    }

    [Fact]
    public void Clone_LeavesAnUnsetStatusUnset() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response);

        var clone = response.Clone(null);

        Assert.Null(clone.Status);
    }

    /// <summary>
    /// A cookie set by a handler has to reach the client, which over HTTP means a Set-Cookie
    /// header on the response.
    /// </summary>
    /// <remarks>
    /// The collection had no reader on this host either: Append stored into a dictionary nothing
    /// serialised, so the call compiled, ran, and the client never saw it. Same defect as the
    /// Kestrel host, and the same one Hardened.Amz fixed on 2026-08-11.
    /// </remarks>
    [Fact]
    public void AppendingACookieWritesASetCookieHeader() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Cookies.Append("session", "abc123");

        Assert.Equal("session=abc123; HttpOnly; Secure", httpContext.Response.Headers["Set-Cookie"]);
    }

    [Fact]
    public void AppendingSeveralCookiesWritesAHeaderForEach() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Cookies.Append("first", "1");
        response.Cookies.Append("second", "2");

        var written = httpContext.Response.Headers["Set-Cookie"];

        Assert.Equal(2, written.Count);
        Assert.Contains(written.ToArray(), v => v!.StartsWith("first=1"));
        Assert.Contains(written.ToArray(), v => v!.StartsWith("second=2"));
    }

    /// <summary>Last write for a name wins, matching the other collections.</summary>
    [Fact]
    public void AppendingTheSameCookieTwiceKeepsOnlyTheLastValue() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Cookies.Append("session", "first");
        response.Cookies.Append("session", "second");

        var written = httpContext.Response.Headers["Set-Cookie"];

        Assert.Equal(1, written.Count);
        Assert.StartsWith("session=second", written.ToString());
    }

    [Fact]
    public void CookieOptionsAreSerialisedOntoTheHeader() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Cookies.Append("session", "abc",
            new CookieSetOptions(Path: "/api", SameSite: SameSite.Strict));

        var written = httpContext.Response.Headers["Set-Cookie"].ToString();

        Assert.Contains("Path=/api", written);
        Assert.Contains("SameSite=Strict", written);
    }

    /// <summary>A response that sets no cookie allocates no collection.</summary>
    [Fact]
    public void AResponseThatSetsNoCookieWritesNoHeader() {
        var httpContext = new DefaultHttpContext();
        _ = new AspNetExecutionResponse(httpContext.Response);

        Assert.False(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }
}
