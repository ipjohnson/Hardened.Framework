using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Headers;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Marks a cross-origin response on a route that declares <see cref="CorsAttribute"/>.
/// </summary>
/// <remarks>
/// It runs ahead of every stage that can refuse a request, so a 401 or a 429 carries the headers a
/// browser needs before it lets a script read the refusal.
/// </remarks>
internal sealed class CorsRouteFilter : IExecutionFilter
{
    private readonly CorsConfiguration _policy;

    public CorsRouteFilter(CorsConfiguration policy)
    {
        _policy = policy;
    }

    public Task Execute(IExecutionChain chain)
    {
        var context = chain.Context;

        if (
            context.Request.Headers.TryGetValue(KnownHeaders.Origin, out var originValues)
            && originValues.ToString() is { Length: > 0 } origin
        )
        {
            // For the reason CorsFilter gives: the answer depends on Origin either way.
            VaryHeader.Add(context.Response.Headers, KnownHeaders.Origin);

            if (_policy.IsOriginAllowed(origin))
            {
                CorsHeaders.WriteActual(context.Response.Headers, _policy, origin);
            }
        }

        return chain.Next();
    }
}
