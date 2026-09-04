using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter;

/// <summary>
/// One filter's place in a handler's chain: how to get the filter, where it runs, and what to call
/// it when the composed chain is written out.
/// </summary>
public class RequestFilterInfo {
    public RequestFilterInfo(Func<IExecutionContext, IExecutionFilter> filterFunc, int? order = null) {
        FilterFunc = filterFunc;
        Order = order;
    }

    /// <param name="filterFunc">What builds the filter for a request.</param>
    /// <param name="order">Where it runs, or null for <see cref="FilterOrder.DefaultValue"/>.</param>
    /// <param name="name">
    /// What the composed-chain log calls it. A registration that gives none is named for whatever
    /// registered it, which reads well for an attribute and less well for a factory method three
    /// calls removed from the filter it builds.
    /// </param>
    public RequestFilterInfo(
        Func<IExecutionContext, IExecutionFilter> filterFunc, int? order, string name)
        : this(filterFunc, order) {
        Name = name;
    }

    public Func<IExecutionContext, IExecutionFilter> FilterFunc { get; }

    public int? Order { get; }

    /// <summary>
    /// What the composed-chain log calls this filter, or null to be named for what registered it.
    /// </summary>
    /// <remarks>
    /// The chain keeps factories rather than filters, so a filter's own type is not known until a
    /// request builds one. The name is what lets the chain be written out as it is composed, once
    /// per handler, rather than on a request. See <c>ExecutionHelper.FilterChainLogCategory</c>.
    /// </remarks>
    public string? Name { get; }
}
