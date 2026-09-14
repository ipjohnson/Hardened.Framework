using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// The third answer to what the handler instance is.
/// </summary>
/// <remarks>
/// <c>InstanceFilter&lt;T&gt;</c> resolves one from the request's scope and
/// <c>StaticInstanceFilter</c> stands in where there is nothing to construct. This one supplies a
/// value the handler was built with, which is what a route registered with a lambda needs: the
/// lambda closes over whatever the registration loop had in hand, so there is nothing for a
/// container to resolve.
/// </remarks>
public class ConstantInstanceFilterTests
{
    [Fact]
    public async Task TheSuppliedValueReachesTheContext()
    {
        var context = Pipeline.Context();
        Func<int, string> handler = value => value.ToString();

        await Pipeline.Chain(context, new ConstantInstanceFilter(handler)).Next();

        Assert.Same(handler, context.HandlerInstance);
    }

    /// <remarks>
    /// Nothing is resolved and nothing can fail, which is the whole difference from
    /// <c>InstanceFilter&lt;T&gt;</c>: there is no container call to refuse.
    /// </remarks>
    [Fact]
    public async Task NothingIsResolved()
    {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, new ConstantInstanceFilter(new object())).Next();

        Assert.Null(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task TheSameValueIsGivenToEveryRequest()
    {
        var handler = new object();
        var filter = new ConstantInstanceFilter(handler);

        var first = Pipeline.Context();
        var second = Pipeline.Context();

        await Pipeline.Chain(first, filter).Next();
        await Pipeline.Chain(second, filter).Next();

        Assert.Same(handler, first.HandlerInstance);
        Assert.Same(handler, second.HandlerInstance);
    }
}
