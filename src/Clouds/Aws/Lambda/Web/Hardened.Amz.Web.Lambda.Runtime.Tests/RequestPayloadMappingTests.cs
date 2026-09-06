using System.Text;
using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// What the handler sees, given what API Gateway sent.
///
/// <para>
/// This is the highest-risk mapping in the repository. Routing is decided from
/// <c>Request.Method</c> and <c>Request.Path</c> alone, so a defect in either turns every route in
/// a deployed application into a 404 with nothing in the logs to explain it — the runtime is doing
/// exactly what it was told, against the wrong path.
/// </para>
///
/// <para>
/// Every case here drives the real <c>ApiGatewayEventProcessor</c>, because the request type it
/// builds is <c>internal</c> and the processor is the only thing that constructs it. See
/// <see cref="ApiGatewayHarness"/>.
/// </para>
/// </summary>
public class RequestPayloadMappingTests {

    /// <summary>
    /// The verb comes from <c>requestContext.http.method</c>, not from a top-level field. Payload
    /// format 2.0 has no <c>httpMethod</c>, which is the trap: reading the wrong one yields null
    /// and matches no route.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task EveryVerbReachesTheHandlerUnchanged(string verb) {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(method: verb));

        Assert.Equal(verb, harness.ExecutionContext.Request.Method);
    }

    [Fact]
    public async Task ThePathComesFromRawPath() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(rawPath: "/orders/42/lines"));

        Assert.Equal("/orders/42/lines", harness.ExecutionContext.Request.Path);
    }

    /// <summary>
    /// A REST API deployed to a stage prefixes every path with the stage name, so a route
    /// registered as <c>/orders</c> arrives as <c>/prod/orders</c>. The prefix is stripped before
    /// routing sees it, or nothing matches once the application is deployed — while everything
    /// matches locally, where there is no stage.
    /// </summary>
    [Fact]
    public async Task TheStagePrefixIsStrippedFromThePath() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(rawPath: "/prod/orders/42", stage: "prod"));

        Assert.Equal("/orders/42", harness.ExecutionContext.Request.Path);
    }

    [Fact]
    public async Task APathIsLeftAloneWhenThereIsNoStage() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(rawPath: "/prod/orders/42"));

        Assert.Equal("/prod/orders/42", harness.ExecutionContext.Request.Path);
    }

    /// <summary>
    /// Only a leading stage segment is a stage. <c>$default</c> is the stage name of an HTTP API
    /// with no explicit stage, and its paths are not prefixed.
    /// </summary>
    [Fact]
    public async Task APathIsLeftAloneWhenItDoesNotStartWithTheStage() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(rawPath: "/orders/42", stage: "prod"));

        Assert.Equal("/orders/42", harness.ExecutionContext.Request.Path);
    }

    [Fact]
    public async Task QueryStringParametersReachTheRequest() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            queryStringParameters: new Dictionary<string, string> {
                ["page"] = "2",
                ["size"] = "50"
            }));

        var queryString = harness.ExecutionContext.Request.QueryString;

        Assert.Equal("2", queryString.Get("page"));
        Assert.Equal("50", queryString.Get("size"));
    }

    /// <summary>
    /// API Gateway omits <c>queryStringParameters</c> entirely when a request carries none, so the
    /// field arrives null rather than empty.
    /// </summary>
    [Fact]
    public async Task AMissingQueryStringIsAnEmptyCollectionRatherThanNull() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(queryStringParameters: null));

        Assert.Equal(0, harness.ExecutionContext.Request.QueryString.Count);
    }

    [Fact]
    public async Task AnAbsentQueryStringKeyReadsAsEmpty() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            queryStringParameters: new Dictionary<string, string> { ["page"] = "2" }));

        Assert.Equal(StringValuesEmpty, harness.ExecutionContext.Request.QueryString.Get("missing"));
    }

    private static readonly Microsoft.Extensions.Primitives.StringValues StringValuesEmpty =
        Microsoft.Extensions.Primitives.StringValues.Empty;

    [Fact]
    public async Task HeadersReachTheRequest() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            headers: new Dictionary<string, string> {
                ["X-Tenant"] = "acme",
                ["Authorization"] = "Bearer token"
            }));

        var headers = harness.ExecutionContext.Request.Headers;

        Assert.Equal("acme", headers["X-Tenant"]);
        Assert.Equal("Bearer token", headers["Authorization"]);
    }

    /// <summary>
    /// Payload format 2.0 folds repeated headers into one comma-joined value before it ever reaches
    /// the function, so this is what a multi-valued header looks like on arrival.
    /// </summary>
    [Fact]
    public async Task ACommaJoinedHeaderArrivesAsSent() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            headers: new Dictionary<string, string> { ["Accept-Encoding"] = "gzip,br" }));

        Assert.Equal("gzip,br", harness.ExecutionContext.Request.Headers["Accept-Encoding"]);
    }

    [Fact]
    public async Task ContentTypeComesFromTheHeaderWhenPresent() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            headers: new Dictionary<string, string> { ["Content-Type"] = "text/csv" }));

        Assert.Equal("text/csv", harness.ExecutionContext.Request.ContentType);
    }

    /// <summary>
    /// A request with no <c>Content-Type</c> is treated as JSON. Returning null instead would leave
    /// the deserialiser with nothing to select on.
    /// </summary>
    [Fact]
    public async Task ContentTypeFallsBackToJson() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.Equal("application/json", harness.ExecutionContext.Request.ContentType);
    }

    [Fact]
    public async Task AcceptComesFromTheHeaderWhenPresent() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            headers: new Dictionary<string, string> { ["Accept"] = "text/html" }));

        Assert.Equal("text/html", harness.ExecutionContext.Request.Accept);
    }

    [Fact]
    public async Task AcceptFallsBackToJson() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.Equal("application/json", harness.ExecutionContext.Request.Accept);
    }

    [Fact]
    public async Task APlainTextBodyIsReadableAsUtf8() {
        var harness = new ApiGatewayHarness();
        string? read = null;

        await harness.Process(
            ApiGatewayHarness.Event(method: "POST", body: """{"sku":"ABC"}"""),
            context => {
                read = new StreamReader(context.Request.Body).ReadToEnd();
                return Task.CompletedTask;
            });

        Assert.Equal("""{"sku":"ABC"}""", read);
    }

    /// <summary>
    /// API Gateway base64-encodes any body it considers binary, and says so with
    /// <c>isBase64Encoded</c>. Handing the encoded text to the handler unchanged would give it
    /// base64 where it expected bytes.
    /// </summary>
    [Fact]
    public async Task ABase64BodyIsDecodedBeforeTheHandlerSeesIt() {
        var harness = new ApiGatewayHarness();
        var payload = new byte[] { 0x00, 0x01, 0xFF, 0x7F, 0x80 };
        byte[]? read = null;

        await harness.Process(
            ApiGatewayHarness.Event(
                method: "POST",
                body: Convert.ToBase64String(payload),
                isBase64Encoded: true),
            context => {
                using var buffer = new MemoryStream();
                context.Request.Body.CopyTo(buffer);
                read = buffer.ToArray();
                return Task.CompletedTask;
            });

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task ABodyStreamIsPositionedAtTheStart() {
        var harness = new ApiGatewayHarness();
        long position = -1;

        await harness.Process(
            ApiGatewayHarness.Event(method: "POST", body: "hello"),
            context => {
                position = context.Request.Body.Position;
                return Task.CompletedTask;
            });

        Assert.Equal(0, position);
    }

    [Fact]
    public async Task AnEmptyBodyIsAnEmptyStreamRatherThanNull() {
        var harness = new ApiGatewayHarness();
        long length = -1;

        await harness.Process(
            ApiGatewayHarness.Event(body: null),
            context => {
                length = context.Request.Body.Length;
                return Task.CompletedTask;
            });

        Assert.Equal(0, length);
    }

    /// <summary>
    /// Payload format 2.0 delivers cookies as their own array rather than folded into a
    /// <c>Cookie</c> header.
    /// </summary>
    [Fact]
    public async Task CookiesReachTheRequestAsSent() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(
            cookies: ["session=abc123", "theme=dark"]));

        Assert.Equal(["session=abc123", "theme=dark"], harness.ExecutionContext.Request.Cookies);
    }

    [Fact]
    public async Task ARequestWithNoCookiesHasNone() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.Empty(harness.ExecutionContext.Request.Cookies);
    }

    [Fact]
    public async Task PathTokensStartEmptyForRoutingToFill() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.Equal(0, harness.ExecutionContext.Request.PathTokens.Count);
    }

    /// <summary>
    /// The Lambda context is published before the chain runs, so a handler resolving
    /// <c>ILambdaContextAccessor</c> gets the invocation it is serving rather than the previous one.
    /// </summary>
    [Fact]
    public async Task TheLambdaContextIsPublishedBeforeTheChainRuns() {
        var harness = new ApiGatewayHarness();
        var lambdaContext = Substitute.For<Amazon.Lambda.Core.ILambdaContext>();
        Amazon.Lambda.Core.ILambdaContext? seen = null;

        await harness.Process(
            ApiGatewayHarness.Event(),
            _ => {
                seen = harness.LambdaContextAccessor.Context;
                return Task.CompletedTask;
            },
            lambdaContext);

        Assert.Same(lambdaContext, seen);
    }

    /// <summary>
    /// The proxy request context carries the authoriser claims, the source IP and the request id.
    /// It is published the same way, and for the same reason.
    /// </summary>
    [Fact]
    public async Task TheProxyRequestContextIsPublishedBeforeTheChainRuns() {
        var harness = new ApiGatewayHarness();
        var request = ApiGatewayHarness.Event();
        request.RequestContext.RequestId = "req-7";
        string? seen = null;

        await harness.Process(request, _ => {
            seen = harness.ProxyRequestContextAccessor.ProxyRequestContext.RequestId;
            return Task.CompletedTask;
        });

        Assert.Equal("req-7", seen);
    }

    [Fact]
    public async Task TheRequestIsLoggedAtBothEnds() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        harness.RequestLogger.Received(1).RequestBegin(Arg.Any<IExecutionContext>());
        harness.RequestLogger.Received(1).RequestEnd(Arg.Any<IExecutionContext>());
    }

    [Fact]
    public async Task TheContextCarriesTheKnownServices() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.Same(harness.KnownServices, harness.ExecutionContext.KnownServices);
    }

    /// <summary>
    /// Every invocation gets its own DI scope, so a scoped service does not leak from one warm
    /// invocation into the next.
    /// </summary>
    [Fact]
    public async Task EachInvocationGetsItsOwnRequestScope() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());
        var first = harness.ExecutionContext.RequestServices;

        await harness.Process(ApiGatewayHarness.Event());
        var second = harness.ExecutionContext.RequestServices;

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task TheRequestDurationIsRecorded() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        harness.MetricLogger.Received(1).Record(
            Hardened.Requests.Abstract.Metrics.RequestMetrics.TotalRequestDuration,
            Arg.Any<double>());
        harness.MetricLogger.Received(1).Dispose();
    }

    /// <summary>
    /// The close-out ran as straight-line statements after the response was encoded, so anything
    /// thrown above took all of it with it. Dispose is what writes the EMF line, so a failed
    /// invocation reported no duration and no metrics at all — on the main production path, and for
    /// exactly the invocations worth measuring.
    /// </summary>
    /// <remarks>
    /// What reaches here is a filter outside <c>ControllerErrorHelper</c>'s handling, or the
    /// response encoding itself; a handler that throws is reported and absorbed inside the chain.
    /// </remarks>
    [Fact]
    public async Task AnInvocationThatThrewIsStillClosedOut() {
        var harness = new ApiGatewayHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Process(
                ApiGatewayHarness.Event(), _ => throw new InvalidOperationException("filter blew up")));

        harness.MetricLogger.Received(1).Record(
            Hardened.Requests.Abstract.Metrics.RequestMetrics.TotalRequestDuration,
            Arg.Any<double>());
        harness.RequestLogger.Received(1).RequestEnd(harness.ExecutionContext);
        harness.MetricLogger.Received(1).Dispose();
    }

    /// <summary>
    /// The host-level failure signal, as on Kestrel and both streaming engines. Nothing else was
    /// reporting an exception that escaped the chain.
    /// </summary>
    [Fact]
    public async Task AnInvocationThatThrewIsReportedAgainstItsContext() {
        var harness = new ApiGatewayHarness();
        var failure = new InvalidOperationException("filter blew up");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Process(ApiGatewayHarness.Event(), _ => throw failure));

        harness.RequestLogger.Received(1).RequestFailed(harness.ExecutionContext, failure);
    }

    /// <summary>
    /// Rethrown rather than turned into a 200 with an empty body: the Lambda runtime marking the
    /// invocation failed is the existing contract, and answering anyway would hide the failure from
    /// retries and the dead-letter queue.
    /// </summary>
    [Fact]
    public async Task AnInvocationThatThrewStillFailsTheInvocation() {
        var harness = new ApiGatewayHarness();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Process(
                ApiGatewayHarness.Event(), _ => throw new InvalidOperationException("filter blew up")));

        Assert.Equal("filter blew up", thrown.Message);
    }

    /// <summary>
    /// Two invocations of the same warm function must not see each other's bodies. The processor
    /// leases its buffers from a pool, and a lease returned without being cleared is how one
    /// request ends up answering with another's data.
    /// </summary>
    [Fact]
    public async Task ASecondInvocationDoesNotSeeTheFirstRequestBody() {
        var harness = new ApiGatewayHarness();
        var bodies = new List<string>();

        Task Read(IExecutionContext context) {
            bodies.Add(new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEnd());
            return Task.CompletedTask;
        }

        await harness.Process(ApiGatewayHarness.Event(method: "POST", body: "first"), Read);
        await harness.Process(ApiGatewayHarness.Event(method: "POST", body: "2nd"), Read);

        Assert.Equal(["first", "2nd"], bodies);
    }
}
