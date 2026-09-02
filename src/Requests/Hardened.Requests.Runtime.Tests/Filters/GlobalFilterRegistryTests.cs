using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// The registry is how an application adds a filter to handlers it does not own - metrics,
/// authentication, tenant resolution. It has two registration overloads with different
/// reaches: one applies a filter to every handler, the other is asked per handler and can
/// decline.
/// </summary>
public class GlobalFilterRegistryTests {

    private static GlobalFilterRegistry Registry(params IRequestFilterProvider[] providers) =>
        new(providers);

    private static IExecutionRequestHandlerInfo Handler(string path = "/orders", string method = "GET") =>
        new ExecutionRequestHandlerInfo(path, method, typeof(GlobalFilterRegistryTests), "Invoke");

    /// <summary>
    /// An application that registers nothing gets nothing, rather than a null the caller has
    /// to guard.
    /// </summary>
    [Fact]
    public void AnEmptyRegistryReturnsAnEmptyFilterList() {
        Assert.Empty(Registry().GetFilters(Handler()));
    }

    /// <summary>
    /// The instance overload applies its filter to every handler, whatever the route.
    /// </summary>
    [Theory]
    [InlineData("/orders", "GET")]
    [InlineData("/orders/{id}", "DELETE")]
    [InlineData("/", "POST")]
    public void AFilterRegisteredByInstanceAppliesToEveryHandler(string path, string method) {
        var filter = Substitute.For<IExecutionFilter>();
        var registry = Registry();

        registry.RegisterFilter(filter);

        var filterInfo = Assert.Single(registry.GetFilters(Handler(path, method)));

        Assert.Same(filter, filterInfo.FilterFunc(Pipeline.Context()));
    }

    /// <summary>
    /// The instance overload defaults to <see cref="FilterOrder.DefaultValue"/>, which puts
    /// the filter after serialization and before the handler.
    /// </summary>
    [Fact]
    public void AFilterRegisteredWithoutAnOrderTakesTheDefaultOrder() {
        var registry = Registry();

        registry.RegisterFilter(Substitute.For<IExecutionFilter>());

        Assert.Equal(FilterOrder.DefaultValue, Assert.Single(registry.GetFilters(Handler())).Order);
    }

    [Theory]
    [InlineData(FilterOrder.HandlerCreation)]
    [InlineData(FilterOrder.Authentication)]
    [InlineData(FilterOrder.Serialization)]
    [InlineData(FilterOrder.Validation)]
    [InlineData(FilterOrder.Before + FilterOrder.Serialization)]
    [InlineData(FilterOrder.DefaultValue)]
    public void AnExplicitOrderIsCarriedThroughToTheFilterInfo(int order) {
        var registry = Registry();

        registry.RegisterFilter(Substitute.For<IExecutionFilter>(), order);

        Assert.Equal(order, Assert.Single(registry.GetFilters(Handler())).Order);
    }

    /// <summary>
    /// The same instance is handed to every context, so a filter registered this way is shared
    /// across requests and must be stateless. Pinned because the alternative - a fresh
    /// instance per request - would make per-request state look safe.
    /// </summary>
    [Fact]
    public void AFilterRegisteredByInstanceIsSharedAcrossRequests() {
        var filter = Substitute.For<IExecutionFilter>();
        var registry = Registry();

        registry.RegisterFilter(filter);

        var filterFunc = Assert.Single(registry.GetFilters(Handler())).FilterFunc;

        Assert.Same(filterFunc(Pipeline.Context()), filterFunc(Pipeline.Context()));
    }

    /// <summary>
    /// The per-handler overload is asked about each handler in turn and sees that handler's
    /// info, which is what lets it decide by route, verb or metadata.
    /// </summary>
    [Fact]
    public void ThePerHandlerOverloadSeesTheHandlerItIsBeingAskedAbout() {
        var seen = new List<string>();
        var registry = Registry();

        registry.RegisterFilter(info => {
            seen.Add($"{info.Method} {info.Path}");

            return null;
        });

        registry.GetFilters(Handler("/orders", "GET"));
        registry.GetFilters(Handler("/orders/{id}", "DELETE"));

        Assert.Equal(new[] { "GET /orders", "DELETE /orders/{id}" }, seen);
    }

    /// <summary>
    /// Returning null is how the per-handler overload declines: the handler gets no filter at
    /// all rather than a filter that has to check whether it applies on every request.
    /// </summary>
    [Fact]
    public void ThePerHandlerOverloadDeclinesByReturningNull() {
        var registry = Registry();

        registry.RegisterFilter(_ => null);

        Assert.Empty(registry.GetFilters(Handler()));
    }

    /// <summary>
    /// The interesting case for the null skip: one registration, some handlers filtered and
    /// some not. A skip that leaked a null into the filter list would fail at chain
    /// construction for every handler that opted out.
    /// </summary>
    [Fact]
    public void ThePerHandlerOverloadCanFilterOneHandlerAndSkipAnother() {
        var filter = Substitute.For<IExecutionFilter>();
        var registry = Registry();

        registry.RegisterFilter(info =>
            info.Method == "POST" ? new RequestFilterInfo(_ => filter) : null);

        Assert.Single(registry.GetFilters(Handler("/orders", "POST")));
        Assert.Empty(registry.GetFilters(Handler("/orders", "GET")));
    }

    /// <summary>
    /// Registrations accumulate. Two calls mean two filters, not the second replacing the
    /// first.
    /// </summary>
    [Fact]
    public void EveryRegistrationContributesItsOwnFilter() {
        var registry = Registry();

        registry.RegisterFilter(Substitute.For<IExecutionFilter>(), 1);
        registry.RegisterFilter(Substitute.For<IExecutionFilter>(), 2);
        registry.RegisterFilter(_ => new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>(), 3));

        Assert.Equal(3, registry.GetFilters(Handler()).Count);
    }

    /// <summary>
    /// Providers supplied at construction - the application's own
    /// <see cref="IRequestFilterProvider"/> registrations - are queried alongside anything
    /// registered afterwards.
    /// </summary>
    [Fact]
    public void ProvidersSuppliedAtConstructionAreQueriedToo() {
        var constructed = Substitute.For<IRequestFilterProvider>();
        constructed.GetFilters(Arg.Any<IExecutionRequestHandlerInfo>())
            .Returns(new[] { new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>(), 1) });

        var registry = Registry(constructed);

        registry.RegisterFilter(Substitute.For<IExecutionFilter>(), 2);

        Assert.Equal(2, registry.GetFilters(Handler()).Count);
    }

    /// <summary>
    /// A provider may contribute several filters for one handler, and all of them are kept.
    /// </summary>
    [Fact]
    public void AProviderContributingSeveralFiltersHasAllOfThemKept() {
        var provider = Substitute.For<IRequestFilterProvider>();
        provider.GetFilters(Arg.Any<IExecutionRequestHandlerInfo>())
            .Returns(new[] {
                new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>(), 1),
                new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>(), 2),
                new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>(), 3)
            });

        Assert.Equal(3, Registry(provider).GetFilters(Handler()).Count);
    }

    /// <summary>
    /// Each call gets its own list. The list is handed to <c>ExecutionHelper</c>, which adds
    /// the three pipeline filters to it - a shared list would accumulate them once per handler
    /// in the application.
    /// </summary>
    [Fact]
    public void EachCallReturnsAFreshListTheCallerCanAddTo() {
        var registry = Registry();

        registry.RegisterFilter(Substitute.For<IExecutionFilter>());

        var first = registry.GetFilters(Handler());

        first.Add(new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>()));

        Assert.Single(registry.GetFilters(Handler()));
    }

    /// <summary>
    /// The per-handler function is consulted on every request-handler pair rather than
    /// memoised, so a registration that decides on mutable application state stays live.
    /// </summary>
    [Fact]
    public void ThePerHandlerFunctionIsConsultedEveryTime() {
        var calls = 0;
        var registry = Registry();

        registry.RegisterFilter(_ => {
            calls++;

            return null;
        });

        registry.GetFilters(Handler());
        registry.GetFilters(Handler());
        registry.GetFilters(Handler());

        Assert.Equal(3, calls);
    }
}
