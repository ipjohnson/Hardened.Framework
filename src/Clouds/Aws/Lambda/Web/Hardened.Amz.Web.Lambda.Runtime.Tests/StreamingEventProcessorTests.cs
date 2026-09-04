using System.Net;
using System.Text;
using Amazon.Lambda.Core;
using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Metrics;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// Stream mode, end to end through the processor: the prelude the first byte commits, the bytes
/// that follow it, and what a failure looks like on either side of that byte.
/// </summary>
public class StreamingEventProcessorTests {

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// The stream opens at the first body byte, and the prelude carries the status, headers and
    /// cookies as they stand at that moment - which is after the pipeline has finished deciding
    /// them, and is the whole reason the prelude is lazy.
    /// </summary>
    [Fact]
    public async Task TheFirstBodyByteOpensTheStreamWithTheStatusHeadersAndCookiesAsTheyStand() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            context.Response.Status = 201;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Request"] = "abc";
            context.Response.Cookies.Append("session", "xyz", new CookieSetOptions(Path: "/"));

            await context.Response.Body.WriteAsync(Bytes("{\"ok\":true}"));
        });

        var prelude = harness.Streams.Prelude;

        Assert.Equal(HttpStatusCode.Created, prelude.StatusCode);
        Assert.Equal("application/json", prelude.Headers["Content-Type"]);
        Assert.Equal("abc", prelude.Headers["X-Request"]);
        var cookie = Assert.Single(prelude.Cookies);
        Assert.StartsWith("session=xyz", cookie);
        Assert.Contains("Path=/", cookie);
        Assert.Equal("{\"ok\":true}", harness.Body);
    }

    /// <summary>
    /// A header set after the first byte is too late: the prelude has gone. That is the contract
    /// every host with a started response has, and <c>ResponseStarted</c> is how a filter asks.
    /// </summary>
    [Fact]
    public async Task AHeaderSetAfterTheFirstByteDoesNotReachThePrelude() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            await context.Response.Body.WriteAsync(Bytes("first"));

            context.Response.Headers["X-Late"] = "too late";
        });

        Assert.False(harness.Streams.Prelude.Headers.ContainsKey("X-Late"));
    }

    [Fact]
    public async Task ResponseStartedIsFalseBeforeTheFirstByteAndTrueAfterIt() {
        var harness = new StreamingHarness();
        var before = true;
        var after = false;

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            before = context.Response.ResponseStarted;
            await context.Response.Body.WriteAsync(Bytes("x"));
            after = context.Response.ResponseStarted;
        });

        Assert.False(before);
        Assert.True(after);
    }

    /// <summary>
    /// A streamed response with an empty body leaves CloudFront waiting for data that never
    /// comes, so a response that ends with nothing written still opens the stream - with the
    /// final status - and writes a newline.
    /// </summary>
    [Fact]
    public async Task AResponseWithNoBodyOpensTheStreamAtTheEndAndWritesANewline() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Status = 404;

            return Task.CompletedTask;
        });

        Assert.Equal(HttpStatusCode.NotFound, harness.Streams.Prelude.StatusCode);
        Assert.Equal("\n", harness.Body);
    }

    /// <summary>
    /// Null means "handled, no opinion" and zero is not a status a handler can have meant; both are
    /// 200 on the wire, as on the buffered path.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task AnUnsetOrZeroStatusIsSentAs200(int? status) {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            context.Response.Status = status;

            await context.Response.Body.WriteAsync(Bytes("body"));
        });

        Assert.Equal(HttpStatusCode.OK, harness.Streams.Prelude.StatusCode);
    }

    /// <summary>
    /// A flushed item reaches the Lambda stream while the handler is still running, which is what
    /// makes a streaming operation stream. The hand-rolled host held it for up to 100 ms.
    /// </summary>
    [Fact]
    public async Task AFlushedItemReachesTheStreamBeforeTheHandlerContinues() {
        var harness = new StreamingHarness();
        string? seenAfterFlush = null;

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            await context.Response.Body.WriteAsync(Bytes("data: 1\n\n"));
            await context.Response.Body.FlushAsync();

            await harness.Streams.Target.FirstWrite.WaitAsync(TimeSpan.FromSeconds(5));

            seenAfterFlush = harness.Body;

            await context.Response.Body.WriteAsync(Bytes("data: 2\n\n"));
        });

        Assert.Equal("data: 1\n\n", seenAfterFlush);
        Assert.Equal("data: 1\n\ndata: 2\n\n", harness.Body);
    }

    /// <summary>
    /// Serializers write synchronously and may never flush; their bytes go with the invocation.
    /// </summary>
    [Fact]
    public async Task SynchronousWritesReachTheStreamByTheEndOfTheInvocation() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), context => {
            context.Response.Body.Write(Bytes("sync"), 0, 4);
            context.Response.Body.WriteByte((byte)'!');

            return Task.CompletedTask;
        });

        Assert.Equal("sync!", harness.Body);
    }

    /// <summary>
    /// A throw after the first byte leaves the processor as the exception, for the bootstrap to
    /// write as trailers and record as a failed invocation. The bytes before it have gone, so the
    /// client sees the handler's output up to the point it broke. The hand-rolled host aborted the
    /// POST silently and reported nothing.
    /// </summary>
    [Fact]
    public async Task AFailureAfterTheFirstByteLeavesAsTheExceptionWithTheBytesBeforeItSent() {
        var harness = new StreamingHarness();
        var failure = new InvalidOperationException("mid-stream");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Process(ApiGatewayHarness.Event(), async context => {
                await context.Response.Body.WriteAsync(Bytes("partial"));

                throw failure;
            }));

        Assert.Same(failure, thrown);
        Assert.Equal("partial", harness.Body);
        harness.RequestLogger.Received(1).RequestFailed(harness.ExecutionContext, failure);
        harness.RequestLogger.Received(1).RequestEnd(harness.ExecutionContext);
    }

    /// <summary>
    /// A throw before any byte opens no stream at all, so the bootstrap reports it through the
    /// error endpoint as a failed invocation rather than as a truncated body.
    /// </summary>
    [Fact]
    public async Task AFailureBeforeTheFirstByteOpensNoStream() {
        var harness = new StreamingHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Process(ApiGatewayHarness.Event(), _ => throw new InvalidOperationException("early")));

        Assert.Empty(harness.Streams.Preludes);
        Assert.Equal(0, harness.Streams.Target.Length);
    }

    /// <summary>
    /// The pump's own failure - the runtime refusing a write - is the invocation's failure when
    /// nothing else went wrong.
    /// </summary>
    [Fact]
    public async Task AWriteTheRuntimeRefusesSurfacesFromTheInvocation() {
        var harness = new StreamingHarness();
        harness.Streams.Target.FailWith = new IOException("connection reset");

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            harness.Process(ApiGatewayHarness.Event(), context =>
                context.Response.Body.WriteAsync(Bytes("x")).AsTask()));

        Assert.Equal("connection reset", thrown.Message);
    }

    [Fact]
    public async Task TheRequestLifecycleIsReported() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event());

        harness.RequestLogger.Received(1).RequestBegin(harness.ExecutionContext);
        harness.RequestLogger.Received(1).RequestEnd(harness.ExecutionContext);
        harness.RequestLogger.DidNotReceiveWithAnyArgs().RequestFailed(default!, default!);
    }

    /// <summary>
    /// Dispose is what writes the EMF line. Both outcomes record the duration and flush.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDurationIsRecordedAndMetricsFlushedWhetherOrNotTheChainFails(bool fails) {
        var harness = new StreamingHarness();

        if (fails) {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.Process(ApiGatewayHarness.Event(), _ => throw new InvalidOperationException()));
        }
        else {
            await harness.Process(ApiGatewayHarness.Event());
        }

        harness.MetricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
        harness.MetricLogger.Received(1).Dispose();
    }

    [Fact]
    public async Task TheLambdaAndProxyContextsArePublishedForTheRequest() {
        var harness = new StreamingHarness();
        var lambdaContext = Substitute.For<ILambdaContext>();
        var request = ApiGatewayHarness.Event(stage: "prod");

        await harness.Process(request, lambdaContext: lambdaContext);

        Assert.Same(lambdaContext, harness.LambdaContextAccessor.Context);
        Assert.Same(request.RequestContext, harness.ProxyRequestContextAccessor.ProxyRequestContext);
    }

    /// <summary>
    /// The request side is the buffered path's, so what a handler reads is what API Gateway sent
    /// whichever mode the deployment chose.
    /// </summary>
    [Fact]
    public async Task TheRequestBodyAndPathReachTheHandler() {
        var harness = new StreamingHarness();
        string? body = null;
        string? path = null;

        await harness.Process(
            ApiGatewayHarness.Event(method: "POST", rawPath: "/orders", body: "{\"sku\":\"ABC\"}"),
            context => {
                body = new StreamReader(context.Request.Body).ReadToEnd();
                path = context.Request.Path;

                return Task.CompletedTask;
            });

        Assert.Equal("{\"sku\":\"ABC\"}", body);
        Assert.Equal("/orders", path);
    }

    /// <summary>
    /// A fork writes into the same stream as the response it came from and starts from the same
    /// status, as the buffered response does.
    /// </summary>
    [Fact]
    public async Task ACloneOfTheResponseWritesIntoTheSameStream() {
        var harness = new StreamingHarness();

        await harness.Process(ApiGatewayHarness.Event(), async context => {
            context.Response.Status = 202;

            var clone = context.Response.Clone();

            Assert.Equal(202, clone.Status);
            Assert.Same(context.Response.Body, clone.Body);

            await clone.Body.WriteAsync(Bytes("from the fork"));

            Assert.True(context.Response.ResponseStarted);
        });

        Assert.Equal(HttpStatusCode.Accepted, harness.Streams.Prelude.StatusCode);
        Assert.Equal("from the fork", harness.Body);
    }
}
