using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
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
        Action<IExecutionContext>? headerActions = null) =>
        new(Empty, serialize ?? WriteValue, headerActions);

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
}
