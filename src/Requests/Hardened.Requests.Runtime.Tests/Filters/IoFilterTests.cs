using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// The IO filter is the pipeline's outermost error boundary. Everything below it - parameter
/// binding, every downstream filter, the handler - either produces a response value or an
/// exception, and this filter is what turns either one into a serialized response.
///
/// <para>
/// Its branches are where a request goes from "failed" to "returned a 500 with a body", so
/// they are worth taking one at a time.
/// </para>
/// </summary>
public class IoFilterTests {

    private static readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> Empty =
        _ => Task.FromResult(EmptyParameters.Instance);

    private static IoFilter Filter(
        Func<IExecutionContext, Task<IExecutionRequestParameters>>? deserialize = null,
        Func<IExecutionContext, Task>? serialize = null,
        Action<IExecutionContext>? headerActions = null) =>
        new(deserialize ?? Empty, serialize ?? (_ => Task.CompletedTask), headerActions);

    /// <summary>
    /// The happy path: bind, run the rest of the chain, serialize what came back.
    /// </summary>
    [Fact]
    public async Task ASuccessfulRequestBindsRunsAndSerializesInThatOrder() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var filter = Filter(
            deserialize: _ => {
                log.Add("bind");

                return Task.FromResult(EmptyParameters.Instance);
            },
            serialize: _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            });

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.Equal(new[] { "bind", "handler", "serialize" }, log);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A request that something ahead of this filter already refused does not have its body read.
    ///
    /// <para>
    /// Nothing could fail ahead of this filter before authorization existed, so this branch was
    /// unreachable and the body was read unconditionally. It is reachable now: a requirement over
    /// grants alone is settled before serialization, and the entire reason for putting it there is
    /// that a request presenting no credential must not cost a large deserialization before it is
    /// rejected.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARequestAlreadyRefusedIsNeitherBoundNorHandled() {
        var log = new List<string>();
        var context = Pipeline.Context();
        var bound = false;

        context.Response.ExceptionValue = new InvalidOperationException("refused upstream");

        var filter = Filter(
            deserialize: _ => {
                bound = true;

                return Task.FromResult(EmptyParameters.Instance);
            },
            serialize: _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            });

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.False(bound);
        Assert.DoesNotContain("handler", log);

        // Still serialized, so the refusal reaches the caller rather than dying in the pipeline.
        Assert.Contains("serialize", log);
    }

    /// <summary>
    /// Parameters already bound - by a fork, or by a transport that bound them itself - are
    /// left alone rather than deserialized a second time off a stream that has been consumed.
    /// </summary>
    [Fact]
    public async Task ParametersThatAreAlreadyBoundAreNotBoundAgain() {
        var context = Pipeline.Context();
        context.Request.Parameters = EmptyParameters.Instance;

        var bindCount = 0;

        var filter = Filter(deserialize: _ => {
            bindCount++;

            return Task.FromResult(EmptyParameters.Instance);
        });

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal(0, bindCount);
    }

    /// <summary>
    /// A binding failure is a client error the caller has to hear about, so it is recorded on
    /// the response rather than thrown - and the rest of the chain is skipped, because a
    /// handler cannot run without its parameters.
    /// </summary>
    [Fact]
    public async Task AFailureBindingParametersSkipsTheHandlerAndIsRecordedOnTheResponse() {
        var logger = Substitute.For<IRequestLogger>();
        var handlerRan = false;

        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton(logger));

        var failure = new FormatException("id was not a number");

        var filter = Filter(deserialize: _ => Task.FromException<IExecutionRequestParameters>(failure));

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
    /// A binding failure still produces a response. Skipping serialization here would send an
    /// empty 200 for a request that never bound.
    /// </summary>
    [Fact]
    public async Task AFailureBindingParametersStillSerializesAResponse() {
        var serialized = false;

        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton(Substitute.For<IRequestLogger>()));

        var filter = Filter(
            deserialize: _ => Task.FromException<IExecutionRequestParameters>(new Exception("boom")),
            serialize: _ => {
                serialized = true;

                return Task.CompletedTask;
            });

        await Pipeline.Chain(context, filter).Next();

        Assert.True(serialized);
    }

    /// <summary>
    /// An exception thrown by a filter below the IO filter propagates up the chain and is
    /// caught here. The filters between it and the IO filter do not get to finish - which is
    /// the difference from an exception thrown inside the handler.
    /// </summary>
    [Fact]
    public async Task AnExceptionThrownInAFilterAbortsTheFiltersBelowIt() {
        var log = new List<string>();
        var context = Pipeline.Context();
        var failure = new InvalidOperationException("filter refused");

        await Pipeline.Chain(context,
            Filter(serialize: _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            }),
            new Pipeline.Inline(async c => {
                log.Add("wrapper-enter");

                await c.Next();

                log.Add("wrapper-exit");
            }),
            new Pipeline.Inline(_ => throw failure),
            new Pipeline.Recording(log, "never")).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
        Assert.Equal(new[] { "wrapper-enter", "serialize" }, log);
    }

    /// <summary>
    /// An exception thrown inside the handler is caught by the invoke filter, which records it
    /// on the response and returns normally. Filters wrapping the handler therefore still run
    /// their post-<c>Next</c> code - the opposite of an exception thrown in a filter.
    /// </summary>
    [Fact]
    public async Task AnExceptionThrownInTheHandlerLetsTheFiltersAroundItFinish() {
        var log = new List<string>();
        var failure = new InvalidOperationException("handler failed");

        var logger = Substitute.For<IRequestLogger>();
        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton(logger));

        context.HandlerInstance = new Handler();

        var invokeFilter = new InvokeNoParametersFilter<Handler>((_, _) => throw failure);

        await Pipeline.Chain(context,
            Filter(serialize: _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            }),
            new Pipeline.Inline(async c => {
                log.Add("wrapper-enter");

                await c.Next();

                log.Add("wrapper-exit");
            }),
            invokeFilter).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
        Assert.Equal(new[] { "wrapper-enter", "wrapper-exit", "serialize" }, log);
    }

    /// <summary>
    /// An exception thrown by a <em>filter</em> is reported, the same as one thrown by a handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this was written for. A handler fault reached <c>ControllerErrorHelper</c>, which
    /// logged it; anything thrown earlier in the chain was caught here, recorded on the response and
    /// never mentioned again. A request refused by the validation filter produced <c>started</c>,
    /// <c>mapped</c> and <c>finished status code '500'</c>, with nothing naming the exception - so
    /// the one failure a developer could not see was the one their own filter caused.
    /// </para>
    /// <para>
    /// Wired to the real <c>ExceptionResponseSerializer</c> rather than a substitute, because what
    /// is being asserted is that the two meet. A test that stubbed the serializer would pass against
    /// the defect.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnExceptionThrownByAFilterIsReported() {
        var failure = new InvalidOperationException("thrown by a filter");

        var logger = Substitute.For<IRequestLogger>();
        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton(logger));

        var responseSerializer = Substitute.For<IResponseSerializer>();
        responseSerializer.SerializeResponse(Arg.Any<IExecutionContext>()).Returns(Task.CompletedTask);

        var locator = Substitute.For<ISerializationLocatorService>();
        locator.FindResponseSerializer(Arg.Any<IExecutionContext>()).Returns(responseSerializer);

        var exceptions = new ExceptionResponseSerializer(
            logger, locator, new ExceptionToModelConverter());

        var filter = Filter(serialize: c => exceptions.Handle(c, c.Response.ExceptionValue!));

        await Pipeline.Chain(context, filter, new Pipeline.Inline(_ => throw failure)).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
        Assert.Equal(500, context.Response.Status);

        logger.Received(1).RequestFailed(context, failure);
    }

    /// <summary>
    /// A handler that has written the body itself sets <c>ShouldSerialize</c> false, and the
    /// IO filter must respect it. Serializing over a body that has already been written
    /// produces two payloads concatenated.
    /// </summary>
    [Fact]
    public async Task SerializationIsSkippedWhenTheResponseSaysItShouldNotBeSerialized() {
        var serialized = false;
        var context = Pipeline.Context();

        var filter = Filter(serialize: _ => {
            serialized = true;

            return Task.CompletedTask;
        });

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(c => {
                c.Context.Response.ShouldSerialize = false;

                return Task.CompletedTask;
            })).Next();

        Assert.False(serialized);
    }

    /// <summary>
    /// Configured response headers are applied even when the request failed, so a CORS or
    /// cache header configured for the application is present on error responses too.
    /// </summary>
    [Fact]
    public async Task ConfiguredHeaderActionsRunEvenWhenTheRequestFailed() {
        var context = Pipeline.Context();

        var filter = Filter(headerActions: c => c.Response.Headers["X-Applied"] = "yes");

        await Pipeline.Chain(context, filter,
            new Pipeline.Inline(_ => throw new Exception("failed"))).Next();

        Assert.Equal("yes", context.Response.Headers["X-Applied"].ToString());
        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// No configured headers means no header action at all, and the filter has to cope with
    /// the null rather than assuming one was supplied.
    /// </summary>
    [Fact]
    public async Task AbsentHeaderActionsAreNotAFailure() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, Filter(headerActions: null)).Next();

        Assert.Null(context.Response.ExceptionValue);
    }

    private class Handler { }
}
