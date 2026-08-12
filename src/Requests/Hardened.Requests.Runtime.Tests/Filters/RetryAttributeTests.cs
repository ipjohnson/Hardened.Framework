using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// <c>[Retry]</c> is the declarative face of <see cref="RetryFilter"/>: what a handler author
/// writes, and the only place the filter's position in the pipeline is decided.
/// </summary>
public class RetryAttributeTests {

    private static readonly IExecutionRequestHandlerInfo HandlerInfo =
        new ExecutionRequestHandlerInfo("/orders", "POST", typeof(RetryAttributeTests), "Post");

    private static IExecutionContext ContextWithPool(IMemoryStreamPool pool) =>
        Pipeline.Context(configureServices: services => services.AddSingleton(pool));

    /// <summary>
    /// An unconfigured <c>[Retry]</c> retries three times with half a second between attempts.
    /// These defaults are the shipped contract - a change to either alters the behaviour of
    /// every handler that wrote the attribute bare.
    /// </summary>
    [Fact]
    public void AnUnconfiguredRetryAllowsThreeAttemptsHalfASecondApart() {
        var attribute = new RetryAttribute();

        Assert.Equal(3, attribute.Retries);
        Assert.Equal(500, attribute.SleepTime);
    }

    /// <summary>
    /// The attribute orders its filter ahead of controller creation, so a retried attempt gets
    /// a fresh controller and an unread body. Ordering it any later would retry against
    /// whatever state the failed attempt left on the controller.
    /// </summary>
    [Fact]
    public void RetryIsOrderedAheadOfControllerCreation() {
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        Assert.Equal(FilterOrder.HandlerCreation - 10, filterInfo.Order);
    }

    /// <summary>
    /// One filter per handler, not one per attribute evaluation - the filter array is built
    /// once and a duplicate would double every retry budget.
    /// </summary>
    [Fact]
    public void RetryContributesExactlyOneFilter() {
        Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));
    }

    /// <summary>
    /// The replay buffer is resolved from the request's own service provider rather than
    /// captured at registration, so each request draws from the pool it was given.
    /// </summary>
    [Fact]
    public void TheFilterTakesItsReplayBufferFromTheRequestsServiceProvider() {
        var pool = Substitute.For<IMemoryStreamPool>();
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        var filter = filterInfo.FilterFunc(ContextWithPool(pool));

        Assert.IsType<RetryFilter>(filter);
    }

    /// <summary>
    /// A request whose services have no memory stream pool fails loudly at filter construction
    /// rather than silently skipping the replay and retrying against a consumed body.
    /// </summary>
    [Fact]
    public void AMissingMemoryStreamPoolFailsRatherThanRetryingWithoutReplay() {
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        Assert.Throws<InvalidOperationException>(() => filterInfo.FilterFunc(Pipeline.Context()));
    }

    /// <summary>
    /// The configured retry count reaches the filter, so <c>[Retry(Retries = n)]</c> means n
    /// attempts and not the default three.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task TheConfiguredRetryCountIsTheNumberOfAttempts(int retries) {
        var attribute = new RetryAttribute { Retries = retries, SleepTime = 0 };
        var context = ContextWithPool(new MemoryStreamPool());

        var filter = Assert.Single(attribute.GetFilters(HandlerInfo)).FilterFunc(context);

        var attempts = 0;
        var chain = Substitute.For<IExecutionChain>();

        chain.Context.Returns(context);
        chain.Next().Returns(_ => {
            attempts++;

            throw new InvalidOperationException("always fails");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Execute(chain));

        Assert.Equal(retries, attempts);
    }
}
