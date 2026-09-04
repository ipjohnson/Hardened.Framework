using Hardened.Requests.Abstract.Execution;
using NSubstitute;

namespace Hardened.Requests.Abstract.Tests.Execution;

/// <summary>
/// That <see cref="CancellationScope"/> puts the previous token back, however the span it wraps
/// ends.
/// </summary>
/// <remarks>
/// The restore is the whole reason the type exists rather than a bare setter. Two filters read
/// <c>IExecutionContext.CancellationToken</c> after the inner chain has returned - the conditional
/// GET flush and the response cache's store - so a scope that forgot to restore would hand both of
/// them an already-cancelled token on exactly the requests that took longest.
/// </remarks>
public class CancellationScopeTests {

    /// <summary>
    /// A context whose token slot actually stores what is written to it, which is all the scope
    /// touches.
    /// </summary>
    private static IExecutionContext Context(CancellationToken transport) {
        var context = Substitute.For<IExecutionContext>();

        context.CancellationToken = transport;

        return context;
    }

    [Fact]
    public void TheScopedTokenIsWhatTheContextReturnsInside() {
        using var transport = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource();

        var context = Context(transport.Token);

        using (context.WithCancellation(deadline.Token)) {
            Assert.Equal(deadline.Token, context.CancellationToken);
        }
    }

    [Fact]
    public void TheTransportTokenIsBackAfterANormalReturn() {
        using var transport = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource();

        var context = Context(transport.Token);

        using (context.WithCancellation(deadline.Token)) { }

        Assert.Equal(transport.Token, context.CancellationToken);
    }

    [Fact]
    public void TheTransportTokenIsBackAfterAThrow() {
        using var transport = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource();

        var context = Context(transport.Token);

        void Failing() {
            using (context.WithCancellation(deadline.Token)) {
                throw new InvalidOperationException("the handler failed");
            }
        }

        Assert.Throws<InvalidOperationException>(Failing);

        Assert.Equal(transport.Token, context.CancellationToken);
    }

    /// <summary>
    /// Nested scopes unwind one at a time, so an operation carrying its own deadline inside an
    /// application-wide one leaves the application-wide one in place rather than the transport's.
    /// </summary>
    [Fact]
    public void NestedScopesRestoreOneLevelEach() {
        using var transport = new CancellationTokenSource();
        using var outer = new CancellationTokenSource();
        using var inner = new CancellationTokenSource();

        var context = Context(transport.Token);

        using (context.WithCancellation(outer.Token)) {
            using (context.WithCancellation(inner.Token)) {
                Assert.Equal(inner.Token, context.CancellationToken);
            }

            Assert.Equal(outer.Token, context.CancellationToken);
        }

        Assert.Equal(transport.Token, context.CancellationToken);
    }

    /// <summary>
    /// The setter stays public and the scope is built on it, so a test can drive a request that
    /// starts out cancelled.
    /// </summary>
    [Fact]
    public void ThePreviousTokenIsWhateverWasThereRatherThanNone() {
        using var already = new CancellationTokenSource();
        already.Cancel();

        var context = Context(already.Token);

        using (context.WithCancellation(CancellationToken.None)) {
            Assert.False(context.CancellationToken.IsCancellationRequested);
        }

        Assert.True(context.CancellationToken.IsCancellationRequested);
    }
}
