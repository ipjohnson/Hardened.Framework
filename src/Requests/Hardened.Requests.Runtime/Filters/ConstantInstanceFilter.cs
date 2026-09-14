using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Hands the handler an instance it was built with, at <c>FilterOrder.HandlerCreation</c>.
/// </summary>
/// <remarks>
/// <para>
/// The third answer to "what is the handler instance". <see cref="InstanceFilter{TController}"/>
/// resolves one from the request's scope, <see cref="StaticInstanceFilter"/> stands in where there
/// is nothing to construct, and this one supplies a value the handler was constructed with.
/// </para>
/// <para>
/// It exists for a route registered with a lambda. The lambda closes over whatever the registration
/// loop had in hand - the tenant, the row - so it cannot be resolved from a container and cannot be
/// replaced by a call to a method. The generated invoke method is static, as every generated invoke
/// method is, so the delegate reaches it the way a controller does: as the instance.
/// </para>
/// </remarks>
public class ConstantInstanceFilter : IExecutionFilter
{
    private readonly object _instance;

    public ConstantInstanceFilter(object instance)
    {
        _instance = instance;
    }

    public Task Execute(IExecutionChain chain)
    {
        chain.Context.HandlerInstance = _instance;

        return chain.Next();
    }
}
