using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// A filter that binds a request's parameters and answers the request when binding fails.
/// </summary>
internal interface IBindingFilter
{
    /// <summary>
    /// The filters at <c>FilterOrder.Validation</c> in this handler's chain, asked for their
    /// constraint errors when binding fails.
    /// </summary>
    /// <remarks>
    /// Set once the chain is composed, which is after the binding filter is built.
    /// </remarks>
    IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> ConstraintFilters { set; }
}
