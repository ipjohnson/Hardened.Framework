using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Middleware;

/// <summary>
/// Serializes a response that was decided in the middleware chain, where nothing else would.
/// </summary>
/// <remarks>
/// <para>
/// A filter refuses a request by setting the response and returning without calling <c>Next</c>.
/// In the <em>handler</em> chain that works: the filter at <c>FilterOrder.Serialization</c> turns
/// what is on the response into bytes on its way out, and a filter ahead of it can continue the
/// chain rather than short-circuiting - which is what <c>AuthorizationFilter</c> does - to be sure
/// of reaching it.
/// </para>
/// <para>
/// The middleware chain has no such filter in it. It is a plain list, and a middleware that
/// answered - CORS, a global rate limiter - produced a correct status with an empty body, because
/// the thing that would have written one is inside a handler chain that was never entered. This is
/// that missing half, and it is why the fix belongs here rather than at each of the five hosts.
/// </para>
/// <para>
/// <b>The guard is deliberately narrow.</b> It fires only when something was actually put on the
/// response to be written. Serializing whenever <c>ShouldSerialize</c> was merely still set would
/// reach <see cref="INullValueResponseHandler"/> on the path where nothing matched at all - and
/// that assigns a status by verb, so an unmatched <c>POST</c> would come back 200. That is the
/// defect recorded as item 1 of the Amz feature review, and it is not worth reintroducing one layer
/// up.
/// </para>
/// </remarks>
public class ResponseFinalizerFilter : IExecutionFilter {

    public async Task Execute(IExecutionChain chain) {
        await chain.Next();

        var context = chain.Context;
        var response = context.Response;

        if (!response.ShouldSerialize) {
            return;
        }

        if (response.ResponseValue == null && response.ExceptionValue == null) {
            return;
        }

        // Resolved per request rather than injected, so a host that assembles a middleware service
        // without the serialization stack - the bare pipelines some tests build - is unaffected
        // until it actually produces something needing serialization.
        var serialization = context.RequestServices.GetService<IContextSerializationService>();

        if (serialization == null) {
            return;
        }

        await serialization.SerializeResponse(context);
    }
}
