using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
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

    #region the caller, on the request scope

    /// <summary>
    /// A context whose services are an application's - the request module's own registrations,
    /// which is what puts the holder there.
    /// </summary>
    private static IExecutionContext ApplicationContext() =>
        Pipeline.Context(
            configureServices: services => new HardenedRequestModule().ConfigureServices(services));

    private static ICallerPrincipal Resolved(IExecutionContext context) =>
        context.RequestServices.GetRequiredService<ICurrentCaller>().Principal;

    /// <summary>
    /// H-03. A specification-first handler implements a generated interface, so it cannot take
    /// <c>IExecutionContext</c> as a parameter and had no way to read the caller at all. The
    /// middleware puts the answer on the request scope as well as on the context, which is what
    /// makes <see cref="ICurrentCaller"/> resolvable in one.
    /// </summary>
    [Fact]
    public async Task TheEstablishedCallerIsPutOnTheRequestScope() {
        var context = ApplicationContext();

        await new AuthenticationMiddleware([Source(new CallerPrincipal("test", subject: "ada"))])
            .Execute(Chain(context));

        Assert.Equal("ada", Resolved(context).Subject);
    }

    /// <summary>
    /// Written from the context rather than from the source's return value, so a request no source
    /// answered for reads what the context holds - the anonymous principal, present rather than
    /// null.
    /// </summary>
    [Fact]
    public async Task ARequestNoSourceAnsweredForReadsTheAnonymousPrincipal() {
        var context = ApplicationContext();

        await new AuthenticationMiddleware([Source(null)]).Execute(Chain(context));

        Assert.Same(AnonymousCallerPrincipal.Instance, Resolved(context));
        Assert.False(Resolved(context).IsAuthenticated);
    }

    /// <summary>
    /// One request's caller is not another's, which is the whole reason the holder is scoped.
    /// </summary>
    [Fact]
    public async Task OneRequestsCallerDoesNotReachAnother() {
        var authenticated = ApplicationContext();
        var untouched = ApplicationContext();

        await new AuthenticationMiddleware([Source(new CallerPrincipal("test", subject: "ada"))])
            .Execute(Chain(authenticated));

        Assert.Equal("ada", Resolved(authenticated).Subject);
        Assert.Same(AnonymousCallerPrincipal.Instance, Resolved(untouched));
    }

    /// <summary>
    /// Asked for rather than required: a host that composed this middleware without the request
    /// module's registrations still has a caller to establish.
    /// </summary>
    [Fact]
    public async Task AContextWithoutTheHolderStillAuthenticates() {
        var context = Pipeline.Context();

        await new AuthenticationMiddleware([Source(new CallerPrincipal("test", subject: "ada"))])
            .Execute(Chain(context));

        Assert.Equal("ada", context.CallerPrincipal.Subject);
    }

    #endregion
}
