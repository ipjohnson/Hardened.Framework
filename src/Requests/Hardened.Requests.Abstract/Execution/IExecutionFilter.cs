namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// One step of a request pipeline. It does its work around <c>chain.Next()</c>; returning without
/// calling it short-circuits everything ordered after.
/// </summary>
/// <remarks>
/// Where a filter runs is <c>FilterOrder</c>, which is the only ordering vocabulary there is.
/// <c>ExecutionFilterOrder</c> used to be a second one and was removed: it named positions relative
/// to parameter binding and serialization that had stopped being where it said they were, most
/// plainly at <c>RetryFilter = -5000</c> against <c>FilterOrder.Retry</c> behind serialization.
/// Nothing shipped ever named a member of it.
/// </remarks>
public interface IExecutionFilter {
    Task Execute(IExecutionChain chain);
}
