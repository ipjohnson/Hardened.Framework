using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// <c>[Retry]</c> is the declarative face of <see cref="RetryFilter"/>: what a handler author
/// writes, and the only place the filter's position in the pipeline is decided.
/// </summary>
public class RetryAttributeTests {

    private static readonly IExecutionRequestHandlerInfo HandlerInfo =
        new ExecutionRequestHandlerInfo("/orders", "POST", typeof(RetryAttributeTests), "Post");

    /// <summary>
    /// An unconfigured <c>[Retry]</c> allows three attempts, backs off from half a second, and
    /// gives up after ten. These defaults are the shipped contract - a change to any of them alters
    /// the behaviour of every handler that wrote the attribute bare.
    /// </summary>
    [Fact]
    public void Defaults_AreThreeAttemptsHalfASecondApartWithinTenSeconds() {
        var attribute = new RetryAttribute();

        Assert.Equal(3, attribute.Attempts);
        Assert.Equal(500, attribute.SleepTime);
        Assert.Equal(10_000, attribute.TotalBudget);
        Assert.False(attribute.AllowNonIdempotent);
    }

    /// <summary>
    /// <c>Retries</c> is the older spelling of <c>Attempts</c> and still sets the same value, so
    /// every <c>[Retry(Retries = n)]</c> already written keeps meaning what it meant.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Retries_IsTheSameValueAsAttempts(int value) {
        Assert.Equal(value, new RetryAttribute { Retries = value }.Attempts);
        Assert.Equal(value, new RetryAttribute { Attempts = value }.Retries);
    }

    /// <summary>
    /// The filter is ordered behind the one that turns a failure into a response, which is the
    /// whole reason it works. Ahead of it - where this attribute used to put it - every attempt
    /// looks like a success, because that filter catches the failure and returns normally.
    /// </summary>
    [Fact]
    public void GetFilters_OrdersTheFilterBehindSerialization() {
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        Assert.Equal(FilterOrder.Retry, filterInfo.Order);
        Assert.True(filterInfo.Order > FilterOrder.Serialization);
    }

    /// <summary>
    /// Behind authorization too, so a refusal is not retried. A denial is not transient and
    /// re-deriving it spends the whole budget on the same answer.
    /// </summary>
    [Fact]
    public void GetFilters_OrdersTheFilterBehindAuthorization() {
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        Assert.True(filterInfo.Order > FilterOrder.Authorization);
    }

    /// <summary>
    /// One filter per handler, not one per attribute evaluation - the filter array is built once
    /// and a duplicate would square every retry budget.
    /// </summary>
    [Fact]
    public void GetFilters_ContributesExactlyOneFilter() {
        Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));
    }

    /// <summary>
    /// The filter needs nothing from the request's services. It used to resolve a memory stream
    /// pool to buffer the body for replay; sitting behind serialization, the parameters are already
    /// bound before it runs and there is nothing left to replay.
    /// </summary>
    [Fact]
    public void GetFilters_BuildsTheFilterWithoutResolvingAnyService() {
        var filterInfo = Assert.Single(new RetryAttribute().GetFilters(HandlerInfo));

        Assert.IsType<RetryFilter>(filterInfo.FilterFunc(null!));
    }
}
