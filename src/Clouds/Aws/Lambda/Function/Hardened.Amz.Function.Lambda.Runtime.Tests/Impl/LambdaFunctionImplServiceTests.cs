using System.Text;
using Amazon.Lambda.Core;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Impl;

/// <summary>
/// The entry point every Hardened Lambda function is invoked through: a stream in, a stream out,
/// and an <see cref="ILambdaContext"/> that the rest of the request can reach.
/// </summary>
public class LambdaFunctionImplServiceTests {

    private sealed class Harness {
        public Harness(LambdaResponseMode mode = LambdaResponseMode.Buffered) {
            var services = new ServiceCollection();
            RootProvider = services.BuildServiceProvider();

            Middleware = Substitute.For<IMiddlewareService>();
            Middleware.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
                var context = callInfo.Arg<IExecutionContext>();

                return new TestExecutionChain(context, ctx => {
                    Contexts.Add(ctx);

                    return OnRequest(ctx);
                });
            });

            var metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
            metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(MetricLogger);

            Service = new LambdaFunctionImplService(
                Middleware,
                new MemoryStreamPool(),
                RootProvider,
                Substitute.For<IKnownServices>(),
                Accessor,
                RequestLogger,
                metricLoggerProvider,
                Streams,
                Options.Create<ILambdaResponseModeConfiguration>(
                    new LambdaResponseModeConfiguration { Mode = mode }));
        }

        public ServiceProvider RootProvider { get; }

        public IMiddlewareService Middleware { get; }

        public ILambdaContextAccessor Accessor { get; } = new LambdaContextAccessor();

        public IRequestLogger RequestLogger { get; } = Substitute.For<IRequestLogger>();

        public IMetricLogger MetricLogger { get; } = Substitute.For<IMetricLogger>();

        public CapturingResponseStreamFactory Streams { get; } = new();

        public LambdaFunctionImplService Service { get; }

        public List<IExecutionContext> Contexts { get; } = [];

        public Func<IExecutionContext, Task> OnRequest { get; set; } = _ => Task.CompletedTask;

        public IExecutionContext Single => Assert.Single(Contexts);

        public string Streamed => Encoding.UTF8.GetString(Streams.Target.ToArray());
    }

    private static MemoryStream Payload(string content) {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// The context is published before the chain runs. Handler resolution reads the function name
    /// off the accessor, so a context published afterwards would leave every invocation unable to
    /// find its handler.
    /// </summary>
    [Fact]
    public async Task TheLambdaContextIsPublishedOnTheAccessorForTheRequest() {
        var harness = new Harness();
        var context = new FakeLambdaContext("Process");

        var seenDuringRequest = default(ILambdaContext);
        harness.OnRequest = _ => {
            seenDuringRequest = harness.Accessor.Context;

            return Task.CompletedTask;
        };

        await harness.Service.InvokeFunction(Payload("{}"), context);

        Assert.Same(context, seenDuringRequest);
    }

    /// <summary>
    /// The function name becomes the request path. That is what a generated handler package matches
    /// against, so an invocation of "Process" has to arrive on path "Process".
    /// </summary>
    [Fact]
    public async Task TheFunctionNameBecomesTheRequestPath() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("ProcessOrder"));

        Assert.Equal("ProcessOrder", harness.Single.Request.Path);
    }

    [Fact]
    public async Task TheRequestMethodIsInvoke() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal("Invoke", harness.Single.Request.Method);
    }

    [Fact]
    public async Task TheIncomingStreamIsTheRequestBody() {
        var harness = new Harness();
        var payload = Payload("{\"value\":42}");

        await harness.Service.InvokeFunction(payload, new FakeLambdaContext("Process"));

        Assert.Same(payload, harness.Single.Request.Body);
    }

    /// <summary>
    /// Client-context custom values are the only caller-supplied metadata a direct Lambda invoke
    /// carries, so they are mapped onto request headers where filters can read them.
    /// </summary>
    [Fact]
    public async Task ClientContextCustomValuesArriveAsRequestHeaders() {
        var harness = new Harness();
        var context = new FakeLambdaContext("Process", new Dictionary<string, string> {
            { "tenant", "acme" },
            { "trace", "abc123" }
        });

        await harness.Service.InvokeFunction(Payload("{}"), context);

        Assert.Equal("acme", harness.Single.Request.Headers["tenant"].ToString());
        Assert.Equal("abc123", harness.Single.Request.Headers["trace"].ToString());
    }

    [Fact]
    public async Task AnInvokeWithoutAClientContextStillProducesAHeaderCollection() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Empty(harness.Single.Request.Headers);
    }

    /// <summary>
    /// The returned stream is rewound. Returning it at its write position hands AWS an empty
    /// response body while every assertion about what was written still passes.
    /// </summary>
    [Fact]
    public async Task TheReturnedStreamIsRewoundToTheStartOfWhatTheHandlerWrote() {
        var harness = new Harness();
        harness.OnRequest = async ctx => {
            var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");

            await ctx.Response.Body.WriteAsync(bytes);
        };

        var result = await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal(0, result.Position);
        Assert.Equal("{\"ok\":true}", new StreamReader(result).ReadToEnd());
    }

    [Fact]
    public async Task TheRequestServicesAreAScopeSeparateFromTheRootProvider() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Same(harness.RootProvider, harness.Single.RootServiceProvider);
        Assert.NotSame(harness.RootProvider, harness.Single.RequestServices);
    }

    [Fact]
    public async Task AnExceptionFromTheChainPropagatesToTheCaller() {
        var harness = new Harness();
        harness.OnRequest = _ => throw new InvalidOperationException("chain failed");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process")));
    }

    /// <summary>
    /// In buffered mode nothing opens a Lambda stream, whatever the handler writes. The body is the
    /// returned stream and the bootstrap sends it whole.
    /// </summary>
    [Fact]
    public async Task InBufferedModeNoLambdaStreamIsOpened() {
        var harness = new Harness();
        harness.OnRequest = ctx => ctx.Response.Body.WriteAsync("x"u8.ToArray()).AsTask();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal(0, harness.Streams.PlainStreams);
        Assert.Empty(harness.Streams.Preludes);
    }

    /// <summary>
    /// In stream mode the first body byte opens the Lambda stream, with no prelude - a function
    /// answers an SDK caller, not a front door - and what the handler writes goes to it. The
    /// return value is empty because the bootstrap ignores it once a stream exists.
    /// </summary>
    [Fact]
    public async Task InStreamModeTheFirstBodyByteOpensTheLambdaStreamWithNoPrelude() {
        var harness = new Harness(LambdaResponseMode.Stream);
        harness.OnRequest = async ctx => {
            await ctx.Response.Body.WriteAsync("{\"ok\":"u8.ToArray());
            await ctx.Response.Body.WriteAsync("true}"u8.ToArray());
        };

        var result = await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal(1, harness.Streams.PlainStreams);
        Assert.Empty(harness.Streams.Preludes);
        Assert.Equal("{\"ok\":true}", harness.Streamed);
        Assert.Equal(0, result.Length);
    }

    /// <summary>
    /// A handler that never writes leaves no stream open, so the bootstrap sends the empty return
    /// as an ordinary response rather than waiting for a stream that never starts. The hand-rolled
    /// engine let that invocation time out.
    /// </summary>
    [Fact]
    public async Task InStreamModeAHandlerThatWritesNothingOpensNoStream() {
        var harness = new Harness(LambdaResponseMode.Stream);

        var result = await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal(0, harness.Streams.PlainStreams);
        Assert.Equal(0, result.Length);
    }

    /// <summary>
    /// Synchronous writes reach the stream by the time the invocation returns, so a serializer that
    /// never flushes still sends its whole body.
    /// </summary>
    [Fact]
    public async Task InStreamModeSynchronousWritesReachTheStreamByTheEndOfTheInvocation() {
        var harness = new Harness(LambdaResponseMode.Stream);
        harness.OnRequest = ctx => {
            ctx.Response.Body.Write("sync"u8.ToArray(), 0, 4);
            ctx.Response.Body.WriteByte((byte)'!');

            return Task.CompletedTask;
        };

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.Equal("sync!", harness.Streamed);
    }

    /// <summary>
    /// A throw after the first byte leaves the invocation as the exception, for the bootstrap to
    /// write as trailers; what was written before it has already gone. The hand-rolled engine
    /// swallowed this and the caller saw a truncated body and nothing else.
    /// </summary>
    [Fact]
    public async Task InStreamModeAFailureAfterTheFirstByteStillPropagatesAndTheBytesBeforeItAreSent() {
        var harness = new Harness(LambdaResponseMode.Stream);
        harness.OnRequest = async ctx => {
            await ctx.Response.Body.WriteAsync("partial"u8.ToArray());

            throw new InvalidOperationException("mid-stream");
        };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process")));

        Assert.Equal("mid-stream", failure.Message);
        Assert.Equal("partial", harness.Streamed);
    }

    [Fact]
    public async Task ResponseStartedFollowsTheFirstByteInStreamMode() {
        var harness = new Harness(LambdaResponseMode.Stream);
        var before = true;
        var after = false;

        harness.OnRequest = async ctx => {
            before = ctx.Response.ResponseStarted;
            await ctx.Response.Body.WriteAsync("x"u8.ToArray());
            after = ctx.Response.ResponseStarted;
        };

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        Assert.False(before);
        Assert.True(after);
    }

    /// <summary>
    /// The request lifecycle is reported the way every other host reports it. Until 2026-09-04 the
    /// managed-runtime path told <see cref="IRequestLogger"/> nothing and recorded no duration; only
    /// the streaming engine did, and that engine is gone.
    /// </summary>
    [Fact]
    public async Task TheRequestLifecycleIsReported() {
        var harness = new Harness();

        await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));

        harness.RequestLogger.Received(1).RequestBegin(harness.Single);
        harness.RequestLogger.Received(1).RequestEnd(harness.Single);
        harness.RequestLogger.DidNotReceiveWithAnyArgs().RequestFailed(default!, default!);
    }

    [Fact]
    public async Task AFailedInvocationIsReportedAgainstItsContextAndStillEnded() {
        var harness = new Harness();
        var failure = new InvalidOperationException("chain failed");
        harness.OnRequest = _ => throw failure;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process")));

        harness.RequestLogger.Received(1).RequestFailed(harness.Single, failure);
        harness.RequestLogger.Received(1).RequestEnd(harness.Single);
    }

    /// <summary>
    /// Dispose is what writes the EMF line, so a failed invocation that skipped it would report no
    /// metrics at all - and a failed request is the one whose duration is worth having.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDurationIsRecordedAndTheMetricsFlushedWhetherOrNotTheChainFails(bool fails) {
        var harness = new Harness();

        if (fails) {
            harness.OnRequest = _ => throw new InvalidOperationException("chain failed");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process")));
        }
        else {
            await harness.Service.InvokeFunction(Payload("{}"), new FakeLambdaContext("Process"));
        }

        harness.MetricLogger.Received(1).Record(RequestMetrics.TotalRequestDuration, Arg.Any<double>());
        harness.MetricLogger.Received(1).Dispose();
        Assert.Same(harness.MetricLogger, harness.Single.RequestMetrics);
    }
}
