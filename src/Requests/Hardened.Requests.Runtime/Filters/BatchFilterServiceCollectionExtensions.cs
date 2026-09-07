using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Putting <see cref="BatchExecutionFilter"/> into every handler's chain.
/// </summary>
public static class BatchFilterServiceCollectionExtensions {
    /// <summary>
    /// One instance, because the filter holds nothing. Everything it works on comes off the chain.
    /// </summary>
    private static readonly BatchExecutionFilter Filter = new();

    /// <summary>
    /// Registers the fan-out filter for every handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Globally rather than on the batched routes, because which routes those are is not known
    /// until a request arrives - the filter chain is composed per handler and the batch is a
    /// property of the delivery. The filter answers a type check and calls <c>chain.Next()</c> when
    /// the request is not a batch, so an application with no batched source pays one <c>is</c> per
    /// request.
    /// </para>
    /// <para>
    /// Safe to call more than once, which matters because every batched transport's module calls
    /// it. A second copy in the chain sits inside the first one's forks, and a fork carries a single
    /// item rather than a batch, so it passes straight through.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBatchExecutionFilter(this IServiceCollection services) =>
        services.AddGlobalFilter(
            new SingleFilterProvider(
                _ => new RequestFilterInfo(
                    _ => Filter,
                    FilterOrder.BeforeSerialization,
                    nameof(BatchExecutionFilter))));
}
