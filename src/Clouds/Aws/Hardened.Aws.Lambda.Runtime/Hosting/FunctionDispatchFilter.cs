using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Hosting;

/// <summary>
/// Finds the handler a request routes to and runs its chain.
/// </summary>
/// <remarks>
/// <para>
/// The last filter in the middleware chain, and the counterpart of what
/// <c>WebExecutionHandlerService</c> is for a web host. <c>IFunctionHandlerProvider</c> was
/// generated, registered and called by nothing at all before this - the switch statement the
/// function generator emits existed in every function application and no code path reached it, so a
/// handler was routed to only in a web host.
/// </para>
/// <para>
/// Dispatch is on the path alone. A function route is <c>QUEUE /orders-new</c> or
/// <c>TIMER /nightly</c>, and the source's name is unique within a function - two triggers of
/// different schemes naming the same source would be two handlers for one queue, which is not a
/// thing to route between.
/// </para>
/// </remarks>
public class FunctionDispatchFilter : IExecutionFilter {
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
