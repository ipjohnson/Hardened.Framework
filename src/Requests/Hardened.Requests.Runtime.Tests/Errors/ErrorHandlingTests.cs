using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Errors;

/// <summary>
/// The two places a request that produced nothing useful is turned into an answer: the
/// not-found handler, which runs the routing chain and fills in a status if nothing did, and
/// the controller error helper, which every generated handler funnels its exceptions through.
/// </summary>
public class ErrorHandlingTests {

    // ------------------------------------------------------------- IResourceNotFoundHandler

    /// <summary>
    /// Nothing set a status, so nothing matched the request. 404 is the answer, and it is the
    /// handler's job rather than the router's because a middleware further down may yet have
    /// produced a response.
    /// </summary>
    [Fact]
    public async Task ARequestNothingSetAStatusForBecomesA404() {
        var context = Pipeline.Context(path: "/no-such-route");

        var chain = Pipeline.Chain(context, new Pipeline.Recording(new List<string>(), "router"));

        await new ResourceNotFoundHandler(Pipeline.Logger<ResourceNotFoundHandler>()).Handle(chain);

        Assert.Equal(404, context.Response.Status);
    }

    /// <summary>
    /// A status somebody already set is left alone. Overwriting it would turn every explicit
    /// 204, 302 or 500 into a 404.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(500)]
    public async Task AStatusThatWasAlreadySetSurvives(int status) {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context, new Pipeline.Inline(c => {
            c.Context.Response.Status = status;

            return Task.CompletedTask;
        }));

        await new ResourceNotFoundHandler(Pipeline.Logger<ResourceNotFoundHandler>()).Handle(chain);

        Assert.Equal(status, context.Response.Status);
    }

    /// <summary>
    /// The handler runs the chain before deciding. Deciding first would 404 every request in
    /// the application.
    /// </summary>
    [Fact]
    public async Task TheRestOfTheChainRunsBeforeTheNotFoundDecisionIsMade() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Recording(log, "first"),
            new Pipeline.Recording(log, "second"));

        await new ResourceNotFoundHandler(Pipeline.Logger<ResourceNotFoundHandler>()).Handle(chain);

        Assert.Equal(new[] { "first", "second" }, log);
        Assert.Equal(404, context.Response.Status);
    }

    /// <summary>
    /// An exception thrown while routing propagates rather than being flattened into a 404 -
    /// a broken route is not a missing one.
    /// </summary>
    [Fact]
    public async Task AnExceptionWhileRoutingIsNotTurnedIntoA404() {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(_ => throw new InvalidOperationException("route table broken")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ResourceNotFoundHandler(Pipeline.Logger<ResourceNotFoundHandler>()).Handle(chain));

        Assert.Null(context.Response.Status);
    }

    // ---------------------------------------------------------------- ControllerErrorHelper

    /// <summary>
    /// A handler's exception is recorded on the response for the IO filter to serialize, not
    /// rethrown - the response has to be produced by the same pipeline that produced the
    /// request.
    /// </summary>
    [Fact]
    public async Task AHandlerExceptionIsRecordedOnTheResponseRatherThanRethrown() {
        var context = Pipeline.Context();
        var failure = new InvalidOperationException("handler failed");

        await ControllerErrorHelper.HandleException(context, failure);

        Assert.Same(failure, context.Response.ExceptionValue);
    }

    /// <summary>
    /// The helper records and does not log. Logging is <c>ExceptionResponseSerializer</c>'s, because
    /// that is where every failure arrives - a filter throw and an authorization refusal set the
    /// same field and never come through here.
    /// </summary>
    /// <remarks>
    /// Pinned rather than left implied: this used to log, and a handler fault was the only failure
    /// that produced a line. Restoring it here would report a handler fault twice.
    /// </remarks>
    [Fact]
    public async Task AHandlerExceptionIsNotLoggedHere() {
        var logger = Substitute.For<IRequestLogger>();
        var context = Pipeline.Context(configureServices: services => services.AddSingleton(logger));
        var failure = new InvalidOperationException("handler failed");

        await ControllerErrorHelper.HandleException(context, failure);

        logger.DidNotReceive().RequestFailed(Arg.Any<IExecutionContext>(), Arg.Any<Exception>());
    }

    /// <summary>
    /// A second failure overwrites the first, so the response reports the most recent one. The
    /// alternative - keeping the first - would report a failure that a retry had already
    /// recovered from.
    /// </summary>
    [Fact]
    public async Task TheMostRecentFailureIsTheOneReported() {
        var context = Pipeline.Context();

        await ControllerErrorHelper.HandleException(context, new Exception("first"));
        await ControllerErrorHelper.HandleException(context, new Exception("second"));

        Assert.Equal("second", context.Response.ExceptionValue!.Message);
    }
}
