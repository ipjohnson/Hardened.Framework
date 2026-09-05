using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// Streaming responses. A handler returning <c>IAsyncEnumerable&lt;T&gt;</c> is serialized one
/// item per line as newline-delimited JSON rather than buffered into a single document, so a
/// caller can start reading before the handler has finished producing.
///
/// <para>
/// The shape of the output is the contract: one item per line, and a trailing newline even
/// when nothing was produced.
/// </para>
/// </summary>
public class AsyncEnumerableIoFilterTests {

    private static readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> Empty =
        _ => Task.FromResult(EmptyParameters.Instance);

    /// <summary>
    /// Stands in for the real serializer by writing the response value's text straight to the
    /// body, so the assertions are about the framing the filter adds rather than about JSON.
    /// </summary>
    private static Task WriteValue(IExecutionContext context) {
        var bytes = Encoding.UTF8.GetBytes(context.Response.ResponseValue?.ToString() ?? "");

        return context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
    }

    private static AsyncEnumerableIoFilter<T> Filter<T>(
        Func<IExecutionContext, Task>? serialize = null,
        Action<IExecutionContext>? headerActions = null,
        IStreamFraming? framing = null,
        TimeSpan heartbeat = default) =>
        new(Empty, serialize ?? WriteValue, headerActions, framing, heartbeat);

    private static IStreamFraming Framing(string name) =>
        name == "sse" ? SseFraming.Instance : NdjsonFraming.Instance;

    private static async IAsyncEnumerable<string> Items(params string[] values) {
        foreach (var value in values) {
            await Task.Yield();

            yield return value;
        }
    }

    private static string Body(IExecutionContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [Fact]
    public async Task EachStreamedItemIsWrittenOnItsOwnLine() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("alpha", "beta", "gamma");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("alpha\nbeta\ngamma\n\n", Body(context));
    }

    [Fact]
    public async Task AStreamedResponseIsMarkedAsNewlineDelimitedJson() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("only");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("application/x-ndjson", context.Response.ContentType);
    }

    /// <summary>
    /// The filter serializes the stream itself, so it clears <c>ShouldSerialize</c>: a
    /// transport that serialized again afterwards would append the enumerable's
    /// <c>ToString</c> to the body it had just streamed.
    /// </summary>
    [Fact]
    public async Task StreamingClearsShouldSerializeSoNothingSerializesTheStreamAgain() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("one");

                return Task.CompletedTask;
            })).Next();

        Assert.False(context.Response.ShouldSerialize);
    }

    /// <summary>
    /// A trailing newline is written even for an empty stream, so the body is never
    /// zero-length. Lambda Function URLs do not close the body stream promptly for a
    /// zero-byte response and downstream readers hang waiting for it.
    /// </summary>
    [Fact]
    public async Task AnEmptyStreamStillWritesATrailingNewlineSoTheBodyIsNeverEmpty() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items();

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("\n", Body(context));
    }

    /// <summary>
    /// A handler on a streaming route that returns something other than the stream falls back
    /// to ordinary serialization rather than producing nothing.
    /// </summary>
    [Fact]
    public async Task ANonStreamingResponseValueIsSerializedNormally() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = "not-a-stream";

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("not-a-stream", Body(context));
        Assert.NotEqual("application/x-ndjson", context.Response.ContentType);
    }

    /// <summary>
    /// A stream of the wrong item type is not the stream this filter is generic over, so it
    /// takes the ordinary serialization path rather than streaming items of a type the route
    /// never declared.
    /// </summary>
    [Fact]
    public async Task AStreamOfADifferentItemTypeIsNotStreamed() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<int>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.NotEqual("application/x-ndjson", context.Response.ContentType);
        Assert.DoesNotContain("alpha\n", Body(context));
    }

    /// <summary>
    /// An exception takes precedence over the stream: the error is serialized and no items are
    /// written, so a caller never sees a half-streamed body followed by an error document.
    /// </summary>
    [Fact]
    public async Task AnExceptionIsSerializedInsteadOfTheStream() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("never-written");

                throw new InvalidOperationException("handler failed");
            })).Next();

        Assert.IsType<InvalidOperationException>(context.Response.ExceptionValue);
        Assert.DoesNotContain("never-written", Body(context));
    }

    /// <summary>
    /// A binding failure is recorded and reported the same way it is for a non-streaming
    /// route, and the handler does not run.
    /// </summary>
    [Fact]
    public async Task AFailureBindingParametersSkipsTheStreamEntirely() {
        var logger = Substitute.For<IRequestLogger>();
        var handlerRan = false;

        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton(logger));

        var failure = new FormatException("bad page size");

        var filter = new AsyncEnumerableIoFilter<string>(
            _ => Task.FromException<IExecutionRequestParameters>(failure), WriteValue, null);

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(_ => {
                handlerRan = true;

                return Task.CompletedTask;
            })).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
        Assert.False(handlerRan);

        logger.Received(1).RequestParameterBindFailed(context, failure);
    }

    /// <summary>
    /// A request that something ahead of this filter already refused does not have its body read.
    ///
    /// <para>
    /// This is the streaming route's half of the rule <c>IoFilter</c> follows. A requirement over
    /// grants alone is settled before serialization so that a request presenting no credential
    /// never costs a large deserialization before it is rejected; binding here would hand that
    /// position back for the routes most likely to be carrying a large body.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARequestAlreadyRefusedIsNeitherBoundNorStreamed() {
        var log = new List<string>();
        var context = Pipeline.Context();
        var bound = false;

        context.Response.ExceptionValue = new InvalidOperationException("refused upstream");

        var filter = new AsyncEnumerableIoFilter<string>(
            _ => {
                bound = true;

                return Task.FromResult(EmptyParameters.Instance);
            },
            _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            },
            null);

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(c => {
                log.Add("handler");

                c.Context.Response.ResponseValue = Items("never-written");

                return Task.CompletedTask;
            })).Next();

        Assert.False(bound);
        Assert.DoesNotContain("handler", log);

        // Still serialized, so the refusal reaches the caller rather than dying in the pipeline.
        Assert.Contains("serialize", log);
    }

    /// <summary>
    /// A bind that never happened is not measured. Recording the duration anyway would put a
    /// near-zero on the histogram for every refused request, which reads as a very fast
    /// deserialization rather than as none - and refused requests are exactly the population you
    /// would go to that histogram to explain.
    /// </summary>
    [Fact]
    public async Task ARequestAlreadyRefusedRecordsNoBindDuration() {
        var metrics = Substitute.For<IMetricLogger>();
        var context = Pipeline.Context(metrics: metrics);

        context.Response.ExceptionValue = new InvalidOperationException("refused upstream");

        await Pipeline.Chain(context, Filter<string>()).Next();

        metrics.DidNotReceive().Record(RequestMetrics.ParameterBindDuration, Arg.Any<double>());
    }

    /// <summary>
    /// Configured response headers apply to a streamed response too, and must be applied
    /// before the first item goes out.
    /// </summary>
    [Fact]
    public async Task ConfiguredHeaderActionsApplyToAStreamedResponse() {
        var context = Pipeline.Context();

        var filter = Filter<string>(headerActions: c => c.Response.Headers["X-Stream"] = "yes");

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("item");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("yes", context.Response.Headers["X-Stream"].ToString());
        Assert.Equal("item\n\n", Body(context));
    }

    #region a 204 ends a subscription

    /// <summary>
    /// A handler that has nothing more to say sets 204 from inside its iterator and yields nothing,
    /// and nothing is written: no content type, no completion bytes. Kestrel aborts the connection
    /// on a 204 with a body, and a client reads that as a network error and reconnects - the
    /// opposite of what a 204 is for. Decided in the filter rather than the framing because it
    /// holds for both.
    /// </summary>
    [Theory]
    [InlineData("sse")]
    [InlineData("ndjson")]
    public async Task A204FromTheHandlerWritesNoFramingAndNoCompletion(string framing) {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(framing: Framing(framing)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = EndsSubscription(c.Context);

                return Task.CompletedTask;
            })).Next();

        Assert.Equal(204, context.Response.Status);
        Assert.Null(context.Response.ContentType);
        Assert.Equal("", Body(context));
        Assert.False(context.Response.ShouldSerialize);
    }

    private static async IAsyncEnumerable<string> EndsSubscription(IExecutionContext context) {
        await Task.Yield();

        context.Response.Status = 204;

        yield break;
    }

    #endregion

    #region failures

    /// <summary>
    /// A failure before the first item is an error document under its own status, the same as a
    /// failure in the handler call: the iterator has begun, but nothing has reached the wire, so
    /// there is still a whole response to answer with.
    /// </summary>
    [Fact]
    public async Task AFailureBeforeTheFirstItemIsAnErrorDocumentNotAStream() {
        var serialized = new List<string>();
        var context = Pipeline.Context();

        var filter = new AsyncEnumerableIoFilter<string>(
            Empty,
            c => {
                serialized.Add(c.Response.ExceptionValue?.Message ?? "no failure");

                return Task.CompletedTask;
            },
            null,
            SseFraming.Instance);

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = FailsBeforeFirst();

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("before the first item", Assert.Single(serialized));
        Assert.Null(context.Response.ContentType);
        Assert.Equal("", Body(context));
        Assert.False(context.Response.ShouldSerialize);
    }

    /// <summary>
    /// A failure after the first item ends the stream and reaches the host, because the bytes are
    /// with the client and nothing can take them back. On Kestrel that is a connection abort; to a
    /// client it is the connection ending, which it reconnects from with <c>Last-Event-ID</c>.
    /// </summary>
    [Fact]
    public async Task AFailureAfterTheFirstItemEndsTheStream() {
        var context = Pipeline.Context();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Pipeline.Chain(context, Filter<string>(framing: SseFraming.Instance),
                new Pipeline.Inline(c => {
                    c.Context.Response.ResponseValue = FailsAfterFirst();

                    return Task.CompletedTask;
                })).Next());

        Assert.Equal("data: alpha\n\n", Body(context));
        Assert.Equal(KnownContentType.EventStream, context.Response.ContentType);
    }

    private static async IAsyncEnumerable<string> FailsBeforeFirst() {
        await Task.Yield();

        if (Throw("before the first item")) {
            yield return "never";
        }
    }

    private static async IAsyncEnumerable<string> FailsAfterFirst() {
        yield return "alpha";

        await Task.Yield();

        if (Throw("after the first item")) {
            yield return "never";
        }
    }

    private static bool Throw(string message) => throw new InvalidOperationException(message);

    #endregion

    #region heartbeats

    /// <summary>
    /// A body that says when the heartbeat has been written, so the test can hold the handler
    /// quiet until then and release it afterwards, with no clock and no guessed sleep.
    /// </summary>
    private sealed class WatchedBody : MemoryStream {
        private readonly TaskCompletionSource _heartbeatSeen;

        public WatchedBody(TaskCompletionSource heartbeatSeen) {
            _heartbeatSeen = heartbeatSeen;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            base.Write(buffer, offset, count);

            if (Encoding.UTF8.GetString(ToArray()).Contains(": keep-alive\n\n")) {
                _heartbeatSeen.TrySetResult();
            }
        }
    }

    private static async IAsyncEnumerable<string> Gated(Task release, string value) {
        await release;

        yield return value;
    }

    private static async IAsyncEnumerable<string> QuietThen(TimeSpan quiet, params string[] values) {
        await Task.Delay(quiet, TestContext.Current.CancellationToken);

        foreach (var value in values) {
            yield return value;
        }
    }

    /// <summary>
    /// A handler that is quiet for longer than the interval gets a heartbeat, and the item that
    /// eventually arrives follows it. The handler is released only once the heartbeat has been
    /// observed on the body, so the order is asserted rather than hoped for.
    /// </summary>
    [Fact]
    public async Task AQuietStreamGetsAHeartbeat() {
        var heartbeatSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Pipeline.Context();

        context.Response.Body = new WatchedBody(heartbeatSeen);

        var run = Pipeline.Chain(context,
            Filter<string>(framing: SseFraming.Instance, heartbeat: TimeSpan.FromMilliseconds(10)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Gated(release.Task, "alpha");

                return Task.CompletedTask;
            })).Next();

        await heartbeatSeen.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        release.SetResult();

        await run;

        var body = Body(context);

        Assert.StartsWith(": keep-alive\n\n", body);
        Assert.EndsWith("data: alpha\n\n", body);
        Assert.DoesNotContain(":\n\n", body);
    }

    [Fact]
    public async Task AStreamThatYieldsPromptlyGetsNoHeartbeat() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context,
            Filter<string>(framing: SseFraming.Instance, heartbeat: TimeSpan.FromSeconds(1)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("alpha", "beta");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("data: alpha\n\ndata: beta\n\n", Body(context));
    }

    [Fact]
    public async Task AZeroIntervalMeansNoHeartbeat() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(framing: SseFraming.Instance),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = QuietThen(TimeSpan.FromMilliseconds(100), "alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("data: alpha\n\n", Body(context));
    }

    /// <summary>
    /// The format has no comment syntax, so the framing declines and the filter stops asking.
    /// </summary>
    [Fact]
    public async Task NdjsonGetsNoHeartbeatWhateverTheInterval() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(heartbeat: TimeSpan.FromMilliseconds(10)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = QuietThen(TimeSpan.FromMilliseconds(100), "alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("alpha\n\n", Body(context));
    }

    /// <summary>
    /// A heartbeat is bytes, and the empty-stream comment exists only to put a byte on a body that
    /// has none. A stream that heartbeated and then ended without events writes no second comment.
    /// </summary>
    [Fact]
    public async Task AnEmptyStreamThatHeartbeatedWritesNoCompletionComment() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context,
            Filter<string>(framing: SseFraming.Instance, heartbeat: TimeSpan.FromMilliseconds(10)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = QuietThen(TimeSpan.FromMilliseconds(100));

                return Task.CompletedTask;
            })).Next();

        var body = Body(context);

        Assert.Contains(": keep-alive\n\n", body);
        Assert.DoesNotContain(":\n\n", body);
    }

    #endregion

    #region event-stream headers

    [Fact]
    public async Task AnEventStreamCarriesNoCacheAndNoAccelBuffering() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(framing: SseFraming.Instance),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("no-cache", context.Response.Headers[KnownHeaders.CacheControl].ToString());
        Assert.Equal("no", context.Response.Headers[KnownHeaders.XAccelBuffering].ToString());
    }

    /// <summary>A handler or filter that already said something about caching is not overruled.</summary>
    [Fact]
    public async Task AHandlersOwnCacheControlIsKeptOnAnEventStream() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(framing: SseFraming.Instance),
            new Pipeline.Inline(c => {
                c.Context.Response.Headers[KnownHeaders.CacheControl] = "no-store";
                c.Context.Response.ResponseValue = Items("alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.Equal("no-store", context.Response.Headers[KnownHeaders.CacheControl].ToString());
    }

    /// <summary>
    /// A newline-delimited response is an ordinary representation a cache may keep, so it is left
    /// to say for itself.
    /// </summary>
    [Fact]
    public async Task ANewlineDelimitedStreamCarriesNeitherHeader() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter<string>(),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items("alpha");

                return Task.CompletedTask;
            })).Next();

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.CacheControl));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.XAccelBuffering));
    }

    #endregion

    #region a body that refuses synchronous writes

    private static string Rejecting(IExecutionContext context) =>
        Encoding.UTF8.GetString(((SynchronousWritesRejectedStream)context.Response.Body).ToArray());

    /// <summary>
    /// Every byte a framing writes goes through <c>WriteAsync</c>. The transport behind a real host
    /// refuses a synchronous write, and a framing that wrote its prefix that way answered 500 on
    /// every event stream while passing every test here over a <c>MemoryStream</c>.
    /// </summary>
    [Theory]
    [InlineData("sse", "id: 1\ndata: alpha\n\ndata: beta\n\n")]
    [InlineData("ndjson", "alpha\nbeta\n\n")]
    public async Task AFramingWritesNothingSynchronously(string framing, string expected) {
        var context = Pipeline.Context();

        context.Response.Body = new SynchronousWritesRejectedStream();

        await Pipeline.Chain(context, Filter<object>(framing: Framing(framing)),
            new Pipeline.Inline(c => {
                // An event with an id for the SSE framing, so the field line is written too;
                // plain items for NDJSON, which frames whatever it is handed as it is.
                c.Context.Response.ResponseValue = framing == "sse" ? Events() : Items("alpha", "beta");

                return Task.CompletedTask;
            })).Next();

        Assert.Null(context.Response.ExceptionValue);
        Assert.Equal(expected, Rejecting(context));
    }

    /// <summary>The completion of an empty stream is a write too, in both framings.</summary>
    [Theory]
    [InlineData("sse", ":\n\n")]
    [InlineData("ndjson", "\n")]
    public async Task AnEmptyStreamsCompletionIsWrittenAsynchronously(string framing, string expected) {
        var context = Pipeline.Context();

        context.Response.Body = new SynchronousWritesRejectedStream();

        await Pipeline.Chain(context, Filter<string>(framing: Framing(framing)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Items();

                return Task.CompletedTask;
            })).Next();

        Assert.Null(context.Response.ExceptionValue);
        Assert.Equal(expected, Rejecting(context));
    }

    /// <summary>And so is the heartbeat, which #262 added synchronously beside the others.</summary>
    [Fact]
    public async Task AHeartbeatIsWrittenAsynchronously() {
        var context = Pipeline.Context();

        context.Response.Body = new SynchronousWritesRejectedStream();

        await Pipeline.Chain(context,
            Filter<string>(framing: SseFraming.Instance, heartbeat: TimeSpan.FromMilliseconds(10)),
            new Pipeline.Inline(c => {
                c.Context.Response.ResponseValue = Slowly("late");

                return Task.CompletedTask;
            })).Next();

        Assert.Null(context.Response.ExceptionValue);
        Assert.Contains(": keep-alive\n\n", Rejecting(context));
        Assert.EndsWith("data: late\n\n", Rejecting(context));
    }

    /// <summary>One event carrying an id, one bare, so both the field and the data lines are covered.</summary>
    private static async IAsyncEnumerable<object> Events() {
        yield return new SseItem<string>("alpha", Id: "1");

        await Task.Yield();

        yield return "beta";
    }

    private static async IAsyncEnumerable<string> Slowly(string value) {
        await Task.Delay(100);

        yield return value;
    }

    #endregion
}
