using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The response type on its own, at the edges the pipeline reaches through
/// <see cref="IExecutionResponse"/>.
/// </summary>
public class ApiGatewayV2ExecutionResponseTests {

    private static ApiGatewayV2ExecutionResponse Create(
        APIGatewayHttpApiV2ProxyResponse? proxyResponse = null) =>
        new(proxyResponse ?? new APIGatewayHttpApiV2ProxyResponse());

    /// <summary>
    /// <c>IExecutionResponse.Body</c> is a non-nullable <c>Stream</c>, and every filter in the
    /// pipeline holds the interface. This implementation declared it <c>Stream?</c> until
    /// 2026-08-11 (CS8766), so the contract said "always a stream" while the implementation was
    /// free to return null — a filter writing to the response before the processor assigned a
    /// buffer would have dereferenced it.
    /// </summary>
    [Fact]
    public void ANewResponseHasAStreamRatherThanNull() {
        IExecutionResponse response = Create();

        Assert.NotNull(response.Body);
        Assert.Same(Stream.Null, response.Body);
    }

    [Fact]
    public void ANewResponseHasNotStarted() {
        Assert.False(Create().ResponseStarted);
    }

    [Fact]
    public void AResponseHasStartedOnceItsBodyHasBeenWrittenTo() {
        var response = Create();
        response.Body = new MemoryStream();

        response.Body.WriteByte((byte)'x');

        Assert.True(response.ResponseStarted);
    }

    [Fact]
    public void TheStatusIsReadFromTheProxyResponse() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse { StatusCode = 404 };

        Assert.Equal(404, Create(proxyResponse).Status);
    }

    [Fact]
    public void SettingTheStatusWritesItToTheProxyResponse() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse();

        Create(proxyResponse).Status = 503;

        Assert.Equal(503, proxyResponse.StatusCode);
    }

    /// <summary>
    /// <c>Status</c> is nullable on the interface and an <c>int</c> on the proxy response, so a
    /// null has to become something. 200 is the only choice that does not surface to the caller as
    /// a gateway error.
    /// </summary>
    [Fact]
    public void ClearingTheStatusFallsBackTo200() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse { StatusCode = 404 };

        Create(proxyResponse).Status = null;

        Assert.Equal(200, proxyResponse.StatusCode);
    }

    [Fact]
    public void ContentTypeIsStoredAsAHeader() {
        var response = Create();

        response.ContentType = "text/csv";

        Assert.Equal("text/csv", response.Headers[KnownHeaders.ContentType]);
        Assert.Equal("text/csv", response.ContentType);
    }

    [Fact]
    public void ShouldSerializeStartsOn() {
        Assert.True(Create().ShouldSerialize);
    }

    [Fact]
    public void CloneCarriesTheFlagsAndValuesThePipelineSets() {
        var response = Create();
        response.ResponseValue = "value";
        response.TemplateName = "template";
        response.ShouldCompress = true;
        response.IsBinary = true;
        response.ShouldSerialize = false;

        var clone = response.Clone(null);

        Assert.Equal("value", clone.ResponseValue);
        Assert.Equal("template", clone.TemplateName);
        Assert.True(clone.ShouldCompress);
        Assert.True(clone.IsBinary);
        Assert.False(clone.ShouldSerialize);
    }

    /// <summary>
    /// A fork writes into the same buffer as the response it came from. Handing the clone a fresh
    /// <see cref="Stream.Null"/> instead would make its writes vanish with no error anywhere.
    /// </summary>
    [Fact]
    public void CloneWritesIntoTheSameBody() {
        var body = new MemoryStream();
        var response = Create();
        response.Body = body;

        Assert.Same(body, response.Clone(null).Body);
    }

    /// <summary>
    /// Status lives on the shared proxy response, so a fork that sets it is setting it for the
    /// invocation — which is what makes a retry filter's replacement status take effect.
    /// </summary>
    [Fact]
    public void CloneSharesTheStatusWithTheResponseItCameFrom() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse();
        var response = Create(proxyResponse);

        response.Clone(null).Status = 409;

        Assert.Equal(409, response.Status);
        Assert.Equal(409, proxyResponse.StatusCode);
    }
}

/// <summary>
/// Response cookies, which payload format 2.0 carries as an array rather than repeated
/// <c>Set-Cookie</c> headers.
/// </summary>
public class ApiGatewayV2CookieSetCollectionTests {

    private static ApiGatewayV2CookieSetCollection Create() =>
        new(new APIGatewayHttpApiV2ProxyResponse());

    /// <summary>
    /// This threw <c>NotImplementedException</c> from the first commit until 2026-08-11. Every
    /// other transport implements it, the pipeline calls it through <see cref="ICookieSetCollection"/>
    /// with no way to know it would throw, and so no Hardened application hosted on API Gateway
    /// could set a cookie.
    /// </summary>
    [Fact]
    public void AnAppendedCookieIsRecorded() {
        var cookies = Create();

        cookies.Append("session", "abc123");

        Assert.Equal("abc123", cookies.Cookies["session"].Item1);
    }

    [Fact]
    public void AnAppendedCookieWithNoOptionsGetsTheEmptyOptions() {
        var cookies = Create();

        cookies.Append("session", "abc123");

        Assert.Same(CookieSetOptions.Empty, cookies.Cookies["session"].Item2);
    }

    [Fact]
    public void AnAppendedCookieKeepsItsOptions() {
        var cookies = Create();
        var options = new CookieSetOptions(Path: "/api", SameSite: SameSite.Strict);

        cookies.Append("session", "abc123", options);

        Assert.Same(options, cookies.Cookies["session"].Item2);
    }

    [Fact]
    public void AppendingTheSameNameTwiceKeepsTheLastValue() {
        var cookies = Create();

        cookies.Append("session", "old");
        cookies.Append("session", "new");

        Assert.Equal("new", Assert.Single(cookies.Cookies).Value.Item1);
    }

    [Fact]
    public void ANewCollectionHasNoCookies() {
        Assert.Empty(Create().Cookies);
    }
}
