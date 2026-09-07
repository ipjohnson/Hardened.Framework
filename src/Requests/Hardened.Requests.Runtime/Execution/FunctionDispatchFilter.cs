using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// Finds the handler a request routes to and runs its chain.
/// </summary>
/// <remarks>
/// <para>
/// The function half of <see cref="IHandlerDispatch"/>, and the counterpart of what
/// <c>WebExecutionHandlerService</c> is for web routes. <c>IFunctionHandlerProvider</c> was
/// generated, registered and called by nothing at all before this - the switch statement the
/// function generator emits existed in every function application and no code path reached it, so a
/// handler was routed to only in a web host.
/// </para>
/// <para>
/// Here rather than in a cloud package because nothing about it is cloud-specific: it reads a
/// framework interface and runs a framework chain. The generator registers it beside the provider
/// it dispatches through, so an application that compiled no function handlers does not carry one.
/// </para>
/// <para>
/// Dispatch is on the path alone. A function route is <c>QUEUE /orders-new</c> or
/// <c>TIMER /nightly</c>, and the source's name is unique within a function - two triggers of
/// different schemes naming the same source would be two handlers for one queue, which is not a
/// thing to route between.
/// </para>
/// </remarks>
public class FunctionDispatchFilter : IHandlerDispatch {
    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        var provider = context.RequestServices.GetRequiredService<IFunctionHandlerProvider>();

        var handler = provider.GetFunctionHandler(context.Request.Path, context.RequestServices);

        if (handler == null) {
            // Raised rather than answered with a status: this family rethrows, and a payload
            // arriving at a function with no handler for it means the deployment wired a source
            // the code does not serve. Answering would tell the source it was handled.
            throw new InvalidOperationException(
                $"No handler is registered for {context.Request.Method} {context.Request.Path}. " +
                "An event source is wired to this function that no trigger attribute declared.");
        }

        await handler.GetExecutionChain(context).Next();
    }
}
