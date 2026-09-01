using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The shipped half of authentication: sources establish the caller, the middleware puts the
/// answer on the context ahead of everything that judges it.
/// </summary>
public class AuthenticationMiddlewareTests {

    private static IExecutionChain Chain(IExecutionContext context) {
        var chain = Substitute.For<IExecutionChain>();

        chain.Context.Returns(context);
        chain.Next().Returns(Task.CompletedTask);

        return chain;
    }

    private static IPrincipalSource Source(ICallerPrincipal? answer) {
        var source = Substitute.For<IPrincipalSource>();

        source.Authenticate(Arg.Any<IExecutionContext>())
            .Returns(new ValueTask<ICallerPrincipal?>(answer));

        return source;
    }

    [Fact]
    public async Task TheFirstAnswerWins() {
        var context = Pipeline.Context();
        var first = Source(new CallerPrincipal("test", subject: "one"));
        var second = Source(new CallerPrincipal("test", subject: "two"));

        await new AuthenticationMiddleware([first, second]).Execute(Chain(context));

        Assert.Equal("one", context.CallerPrincipal.Subject);
        await second.DidNotReceive().Authenticate(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// Null means "this request carries nothing of mine", so the next source is asked.
    /// </summary>
    [Fact]
    public async Task ANullAnswerFallsThroughToTheNextSource() {
        var context = Pipeline.Context();
        var declines = Source(null);
        var answers = Source(new CallerPrincipal("test", subject: "two"));

        await new AuthenticationMiddleware([declines, answers]).Execute(Chain(context));

        Assert.Equal("two", context.CallerPrincipal.Subject);
    }

    /// <summary>
    /// A request no source answers for is left exactly as it started, and still reaches the
    /// handler chain - refusing it is authorization's decision, not this middleware's.
    /// </summary>
    [Fact]
    public async Task NoAnswerLeavesTheAnonymousDefaultAndContinues() {
        var context = Pipeline.Context();
        var before = context.CallerPrincipal;
        var chain = Chain(context);

        await new AuthenticationMiddleware([Source(null)]).Execute(chain);

        Assert.Same(before, context.CallerPrincipal);
        Assert.False(context.CallerPrincipal.IsAuthenticated);
        await chain.Received(1).Next();
    }

    /// <summary>
    /// The principal is on the context before the chain continues, so every filter behind this
    /// middleware - both authorization positions included - judges the same caller.
    /// </summary>
    [Fact]
    public async Task ThePrincipalIsOnTheContextBeforeTheChainContinues() {
        var context = Pipeline.Context();
        var chain = Chain(context);
        ICallerPrincipal? seen = null;

        chain.Next().Returns(_ => {
            seen = context.CallerPrincipal;

            return Task.CompletedTask;
        });

        await new AuthenticationMiddleware([Source(new CallerPrincipal("test", subject: "one"))])
            .Execute(chain);

        Assert.Equal("one", seen?.Subject);
    }
}
