using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Kestrel.Runtime.Impl;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// The response adapter, and in particular the status contract.
///
/// <c>ResourceNotFoundHandler</c> supplies a 404 only when it finds the status still unset, so
/// the getter has to distinguish "nothing has decided yet" from "200". Reading the server's
/// <c>StatusCode</c> back — which starts at 200 — collapses that distinction and makes every
/// unmatched route return an empty 200. That is exactly what this adapter did before the
/// integration SUT caught it.
/// </summary>
public class FeatureExecutionResponseTests {

    private static FeatureExecutionResponse Response(ServerFeatures features) =>
        new(features.Response, features.ResponseBody);

    [Fact]
    public void Status_IsNullBeforeAnythingSetsIt() {
        Assert.Null(Response(new ServerFeatures()).Status);
    }

    [Fact]
    public void Status_WritesThroughToTheServer() {
        var features = new ServerFeatures();

        Response(features).Status = 404;

        Assert.Equal(404, features.Response.StatusCode);
    }

    [Fact]
    public void Status_NullResetsTheServerToTwoHundred() {
        var features = new ServerFeatures();
        var response = Response(features);

        response.Status = 500;
        response.Status = null;

        Assert.Null(response.Status);
        Assert.Equal(200, features.Response.StatusCode);
    }

    /// <summary>
    /// Once the response has started the status is settled, so it is reported rather than left
    /// null. Without this, nothing sets a status on an ordinary success path and every successful
    /// request logs a blank status at <c>RequestEnd</c>.
    /// </summary>
    [Fact]
    public void Status_ReportsTheServerStatusOnceTheResponseHasStarted() {
        var features = new ServerFeatures();
        var response = Response(features);

        Assert.Null(response.Status);

        features.Response.StatusCode = 201;
        features.Response.HasStarted = true;

        Assert.Equal(201, response.Status);
    }

    [Fact]
    public void ResponseStarted_TracksTheServerFeature() {
        var features = new ServerFeatures();
        var response = Response(features);

        Assert.False(response.ResponseStarted);

        features.Response.HasStarted = true;

        Assert.True(response.ResponseStarted);
    }

    [Fact]
    public void Body_DefaultsToTheStreamTheServerSupplied() {
        var features = new ServerFeatures();

        Assert.Same(features.Body, Response(features).Body);
    }

    /// <summary>
    /// A filter that swaps the stream — the compression filter does — must be able to read back
    /// what it set, rather than the write going one way and the read the other.
    /// </summary>
    [Fact]
    public void Body_CanBeReplacedByAFilter() {
        var replacement = new MemoryStream();
        var response = Response(new ServerFeatures());

        response.Body = replacement;

        Assert.Same(replacement, response.Body);
    }

    [Fact]
    public void ContentType_WritesThroughToTheResponseHeaders() {
        var features = new ServerFeatures();

        Response(features).ContentType = "application/json";

        Assert.Equal("application/json", features.Response.Headers.ContentType);
    }

    [Fact]
    public void Clone_CarriesTheStatusAndTheBodyOverride() {
        var replacement = new MemoryStream();
        var response = Response(new ServerFeatures());

        response.Status = 418;
        response.Body = replacement;

        var clone = response.Clone();

        Assert.Equal(418, clone.Status);
        Assert.Same(replacement, clone.Body);
    }

    [Fact]
    public void Clone_LeavesAnUnsetStatusUnset() {
        Assert.Null(Response(new ServerFeatures()).Clone().Status);
    }

    /// <summary>
    /// Kestrel needs this. A response that wrote no body never sends its headers otherwise, and
    /// the connection is left waiting on a request the application considers finished.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_CompletesTheServerBody() {
        var features = new ServerFeatures();

        await Response(features).CompleteAsync();

        Assert.Equal(1, features.ResponseBody.CompleteCount);
    }

    /// <summary>
    /// A cookie set by a handler has to reach the client, which over HTTP means a Set-Cookie
    /// header on the response the server writes.
    /// </summary>
    /// <remarks>
    /// The collection had no reader on this host: Append stored into a dictionary nothing
    /// serialised, so the call compiled, ran, and the client never saw it. Hardened.Amz carried the
    /// same defect until 2026-08-11, where it at least threw NotImplementedException rather than
    /// silently accepting the cookie.
    /// </remarks>
    [Fact]
    public void AppendingACookieWritesASetCookieHeader() {
        var features = new ServerFeatures();
        var response = new FeatureExecutionResponse(features.Response, features.ResponseBody);

        response.Cookies.Append("session", "abc123");

        Assert.Equal("session=abc123; HttpOnly; Secure", features.Response.Headers["Set-Cookie"]);
    }

    [Fact]
    public void AppendingSeveralCookiesWritesAHeaderForEach() {
        var features = new ServerFeatures();
        var response = new FeatureExecutionResponse(features.Response, features.ResponseBody);

        response.Cookies.Append("first", "1");
        response.Cookies.Append("second", "2");

        var written = features.Response.Headers["Set-Cookie"];

        Assert.Equal(2, written.Count);
        Assert.Contains(written.ToArray(), v => v!.StartsWith("first=1"));
        Assert.Contains(written.ToArray(), v => v!.StartsWith("second=2"));
    }

    /// <summary>Last write for a name wins, which is the semantic Hardened.Amz documents.</summary>
    [Fact]
    public void AppendingTheSameCookieTwiceKeepsOnlyTheLastValue() {
        var features = new ServerFeatures();
        var response = new FeatureExecutionResponse(features.Response, features.ResponseBody);

        response.Cookies.Append("session", "first");
        response.Cookies.Append("session", "second");

        var written = features.Response.Headers["Set-Cookie"];

        Assert.Equal(1, written.Count);
        Assert.StartsWith("session=second", written.ToString());
    }

    [Fact]
    public void CookieOptionsAreSerialisedOntoTheHeader() {
        var features = new ServerFeatures();
        var response = new FeatureExecutionResponse(features.Response, features.ResponseBody);

        response.Cookies.Append("session", "abc",
            new CookieSetOptions(Path: "/api", SameSite: SameSite.Strict));

        var written = features.Response.Headers["Set-Cookie"].ToString();

        Assert.Contains("Path=/api", written);
        Assert.Contains("SameSite=Strict", written);
    }
}
