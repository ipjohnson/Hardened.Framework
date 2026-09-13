using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Stands in for <see cref="InstanceFilter{TController}"/> where the handler is a static method, at
/// <c>FilterOrder.HandlerCreation</c>.
/// </summary>
/// <remarks>
/// <para>
/// A static handler has no instance to construct, and its declaring type is never registered - a
/// static class cannot be, so <c>GetRequiredService</c> would have nothing to answer with. The
/// generator passes <c>object</c> as the controller type for these, and
/// <see cref="InstanceFilterProvider"/> reads that as the request for this filter.
/// </para>
/// <para>
/// It still assigns <see cref="IExecutionContext.HandlerInstance"/>, because the invoke filters
/// read it back and refuse a null. <see cref="Instance"/> is what they get, and the generated
/// invoke method ignores it.
/// </para>
/// </remarks>
public class StaticInstanceFilter : IExecutionFilter {
    /// <summary>The value a static handler's <c>HandlerInstance</c> is set to.</summary>
    public static readonly object HandlerInstance = new();

    public static readonly StaticInstanceFilter Instance = new();

    public Task Execute(IExecutionChain chain) {
        chain.Context.HandlerInstance = HandlerInstance;

        return chain.Next();
    }
}
