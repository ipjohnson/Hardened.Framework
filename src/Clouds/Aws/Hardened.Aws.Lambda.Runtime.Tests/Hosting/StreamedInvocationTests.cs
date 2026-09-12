using System.Net;
using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Http;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Aws.Lambda.Sqs;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Hosting;

/// <summary>
/// The invocation loop in stream mode: when it opens a Lambda response stream, what the prelude
/// carries when it does, and which functions are left buffered under the same variable.
/// </summary>
/// <remarks>
/// The AWS stream is never reached. <c>LambdaResponseStreamFactory</c> is static and its setter is
/// internal to the AWS packages, so <see cref="IResponseStreamFactory"/> is the seam and
/// <see cref="CapturingResponseStreamFactory"/> is what these assert through: the prelude each
/// stream opened with, and every byte written to it.
/// </remarks>
public class StreamedInvocationTests {

    // ------------------------------------------------------------------ which mode is served

    /// <summary>
    /// The whole of the mode's effect on a web-shaped function: a stream, not an envelope.
    /// </summary>
    [Fact]
    public async Task StreamModeOpensAResponseStream() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        executor.Body = Writes("hello");

        var output = await handler.Invoke(Input(Payloads.HttpJson), Context());

        Assert.Equal("hello", streams.Target.Text);

        // The bootstrap ignores the return value once a stream has been created, so the host does
        // not build a second body to be thrown away.
        Assert.Same(Stream.Null, output);
    }

    /// <summary>
    /// The default, and what every function that names no variable runs under.
    /// </summary>
    [Fact]
    public async Task BufferedModeWritesTheEnvelopeAndOpensNoStream() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Buffered, new LambdaHttpAdapter());

        executor.Body = Writes("hello");

        var output = await handler.Invoke(Input(Payloads.HttpJson), Context());

        Assert.Empty(streams.Preludes);
        Assert.Contains("\"body\":\"hello\"", await Text(output));
    }

    /// <summary>
    /// A queue function under a stream-mode variable stays buffered rather than failing.
    /// </summary>
    /// <remarks>
    /// The variable describes the front door, and an event source has none. Refusing here would
    /// break a deployment that sets the variable account-wide for its HTTP functions and shares the
    /// setting with its queues.
    /// </remarks>
    [Fact]
    public async Task AnAdapterThatCannotStreamStaysBufferedUnderStreamMode() {
        var (handler, _, streams) = Build(LambdaResponseMode.Stream, new SqsAdapter());

        await handler.Invoke(Input(Payloads.SqsJson), Context());

        Assert.Empty(streams.Preludes);
    }

    // ------------------------------------------------------------------ the prelude

    /// <summary>
    /// Status, headers and cookies as they stood at the first byte.
    /// </summary>
    [Fact]
    public async Task ThePreludeCarriesTheStatusHeadersAndCookies() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        executor.Body = async context => {
            context.Response.Status = 201;
            context.Response.Headers["X-Trace"] = "abc";
            context.Response.Cookies.Append("session", "s1");

            await context.Response.Body.WriteAsync("hi"u8.ToArray());
        };

        await handler.Invoke(Input(Payloads.HttpJson), Context());

        var prelude = streams.Prelude;

        Assert.Equal(HttpStatusCode.Created, prelude.StatusCode);
        Assert.Equal("abc", prelude.Headers["X-Trace"]);
        Assert.Contains(prelude.Cookies, cookie => cookie.StartsWith("session=s1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Decided at the first byte rather than when the response was created, which is the whole
    /// reason the stream opens late.
    /// </summary>
    /// <remarks>
    /// A refusal serialized before any handler ran sets its own status and then writes, and this is
    /// what makes the client see that status rather than the 200 the response started with. Set
    /// after a write, it is too late and the prelude has gone - which is the documented cost and
    /// what the 204 rule in the SSE contract exists for.
    /// </remarks>
    [Fact]
    public async Task AStatusSetBeforeTheFirstByteReachesThePrelude() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        executor.Body = async context => {
            await context.Response.Body.WriteAsync("first"u8.ToArray());

            // After the prelude has gone. Recorded on the response, ignored on the wire.
            context.Response.Status = 500;
        };

        await handler.Invoke(Input(Payloads.HttpJson), Context());

        Assert.Equal(HttpStatusCode.OK, streams.Prelude.StatusCode);
    }

    // ------------------------------------------------------------------ the empty and failing cases

    /// <summary>
    /// A response that wrote nothing still opens the stream, and sends one newline.
    /// </summary>
    /// <remarks>
    /// A zero-byte streamed body is not delivered promptly - CloudFront and a function URL both sit
    /// waiting on data that never arrives - so a reader blocks until the invocation times out.
    /// </remarks>
    [Fact]
    public async Task AnEmptyResponseStillOpensTheStream() {
        var (handler, _, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        await handler.Invoke(Input(Payloads.HttpJson), Context());

        Assert.Single(streams.Preludes);
        Assert.Equal("\n", streams.Target.Text);
    }

    /// <summary>
    /// A throw after the first byte keeps what reached the client and fails the invocation.
    /// </summary>
    /// <remarks>
    /// The stream is already open and the status already sent, so there is nothing to take back.
    /// The bootstrap writes the failure as trailers and records the invocation as failed, which is
    /// what a truncated stream should be.
    /// </remarks>
    [Fact]
    public async Task AThrowAfterTheFirstByteKeepsWhatWasWritten() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        executor.Body = async context => {
            await context.Response.Body.WriteAsync("partial"u8.ToArray());

            throw new InvalidOperationException("nothing more to stream");
        };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input(Payloads.HttpJson), Context()));

        Assert.Equal("nothing more to stream", failure.Message);
        Assert.Equal("partial", streams.Target.Text);
    }

    /// <summary>
    /// A throw before the first byte opens no stream at all, so the failure is a complete response
    /// rather than an empty one with a 200 already on it.
    /// </summary>
    [Fact]
    public async Task AThrowBeforeTheFirstByteOpensNoStream() {
        var (handler, executor, streams) = Build(LambdaResponseMode.Stream, new LambdaHttpAdapter());

        executor.Body = _ => throw new InvalidOperationException("nothing to stream");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input(Payloads.HttpJson), Context()));

        Assert.Empty(streams.Preludes);
    }

    // ------------------------------------------------------------------ harness

    private static Func<IExecutionContext, Task> Writes(string body) =>
        async context => await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(body));

    private static async Task<string> Text(Stream output) {
        using var reader = new StreamReader(output);

        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static (LambdaInvocationHandler Handler, RecordingExecutor Executor,
        CapturingResponseStreamFactory Streams) Build(
            LambdaResponseMode mode, params IPayloadAdapter[] adapters) {
        var executor = new RecordingExecutor();
        var streams = new CapturingResponseStreamFactory();

        var services = new ServiceCollection();

        services.AddSingleton<IKnownServices>(new StubKnownServices());
        services.AddSingleton<IMiddlewareService, MiddlewareService>();
        services.AddSingleton<IHandlerDispatch, StubDispatch>();

        var handler = new LambdaInvocationHandler(
            services.BuildServiceProvider(), executor, new NullMetricLoggerProvider(), adapters,
            streams,
            Options.Create<ILambdaResponseModeConfiguration>(
                new LambdaResponseModeConfiguration { Mode = mode }));

        return (handler, executor, streams);
    }

    private static ILambdaContext Context() =>
        new TestLambdaContext(remainingTime: TimeSpan.FromSeconds(30));

    private static Stream Input(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    /// <summary>Runs whatever a test put in <see cref="Body"/> in place of the real pipeline.</summary>
    private sealed class RecordingExecutor : IRequestExecutor {
        public Func<IExecutionContext, Task>? Body;

        public void Begin(IExecutionContext context) { }

        public Task RunChain(IExecutionContext context, HostFailurePolicy onFailure) => Task.CompletedTask;

        public void End(IExecutionContext context) { }

        public Task Run(IExecutionContext context, HostFailurePolicy onFailure) =>
            Body?.Invoke(context) ?? Task.CompletedTask;
    }

    private sealed class StubDispatch : IHandlerDispatch {
        public Task Execute(IExecutionChain chain) => Task.CompletedTask;
    }
}
