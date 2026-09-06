using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using NSubstitute;
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

    /// <summary>
    /// The case this transport got wrong until 2026-08-15, and the reason it returned an empty 200
    /// for every unmatched route: <c>ResourceNotFoundHandler</c> supplies a 404 only when it finds
    /// the status still unset, and a getter reading straight off the proxy response can never
    /// report that — <c>StatusCode</c> is a non-nullable <c>int</c> starting at 0.
    /// </summary>
    [Fact]
    public void TheStatusStartsUnsetSoTheNotFoundHandlerCanSeeIt() {
        Assert.Null(Create(new APIGatewayHttpApiV2ProxyResponse()).Status);
    }

    /// <summary>
    /// A status already on the proxy response is not the pipeline's doing, and reporting it would
    /// put the transport back where it started.
    /// </summary>
    [Fact]
    public void TheStatusStaysUnsetEvenWhenTheProxyResponseCarriesOne() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse { StatusCode = 404 };

        Assert.Null(Create(proxyResponse).Status);
    }

    [Fact]
    public void SettingTheStatusWritesItToTheProxyResponse() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse();
        var response = Create(proxyResponse);

        response.Status = 503;

        Assert.Equal(503, proxyResponse.StatusCode);
        Assert.Equal(503, response.Status);
    }

    /// <summary>
    /// <c>Status</c> is nullable on the interface and an <c>int</c> on the proxy response, so a
    /// null has to become something on the wire. 200 is the only choice that does not surface to
    /// the caller as a gateway error.
    /// </summary>
    [Fact]
    public void ClearingTheStatusFallsBackTo200OnTheWireAndReadsBackAsUnset() {
        var proxyResponse = new APIGatewayHttpApiV2ProxyResponse { StatusCode = 404 };
        var response = Create(proxyResponse);
        response.Status = 500;

        response.Status = null;

        Assert.Equal(200, proxyResponse.StatusCode);
        Assert.Null(response.Status);
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
        var output = Substitute.For<IHardenedResponseOutput>();
        Func<IExecutionContext, IHardenedResponseOutput> factory = _ => output;

        response.Output = output;
        response.OutputFactory = factory;
        response.IsBinary = true;
        response.ShouldSerialize = false;

        var clone = response.Clone(null);

        Assert.Equal("value", clone.ResponseValue);
        Assert.Same(output, clone.Output);
        Assert.Same(factory, clone.OutputFactory);
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
    /// A clone starts where the original stands, as <c>LambdaExecutionResponse</c> and the
    /// framework's <c>FeatureExecutionResponse</c> both do.
    /// </summary>
    [Fact]
    public void CloneStartsFromTheStatusTheOriginalHad() {
        var response = Create();
        response.Status = 201;

        Assert.Equal(201, response.Clone(null).Status);
    }

    /// <summary>
    /// And diverges from there. This used to be shared, but only as a side effect of the getter
    /// reading off the proxy response — the same accident that cost the transport its 404s. The
    /// wire value still moves, because the proxy response is shared; what a fork must not do is
    /// silently rewrite what the original reports about itself.
    /// </summary>
    [Fact]
    public void CloneStatusDoesNotWriteBackToTheResponseItCameFrom() {
        var response = Create();
        response.Status = 200;

        response.Clone(null).Status = 409;

        Assert.Equal(200, response.Status);
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
