using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// The policy a preflight is answered with once routes declare CORS: the one declared by the route
/// the preflight asks about.
/// </summary>
/// <remarks>
/// The route's own metadata is read first, and it lists the method's declarations ahead of the
/// class's. The module's declaration reaches a handler's metadata only as its filter chain is
/// built, so it is looked up here by the handler's assembly, the way <c>ExecutionHelper</c> merges
/// it.
/// </remarks>
internal sealed class RouteCorsPolicies
{
    private readonly IServiceProvider _services;

    public RouteCorsPolicies(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// The policy <paramref name="handler"/> declares, or null where it declares none.
    /// </summary>
    public CorsConfiguration? For(IExecutionRequestHandler? handler)
    {
        if (handler == null)
        {
            return null;
        }

        var handlerInfo = handler.HandlerInfo;

        var declared =
            CorsAttribute.Nearest(handlerInfo.Metadata) ?? ModuleDeclaration(handlerInfo);

        return declared?.Policy(_services);
    }

    private CorsAttribute? ModuleDeclaration(IExecutionRequestHandlerInfo handlerInfo)
    {
        var registered = _services.GetService<IEnumerable<IApplicationFilterDeclarations>>();

        if (registered == null)
        {
            return null;
        }

        foreach (var declarations in registered)
        {
            if (
                Equals(declarations.DeclaringAssembly, handlerInfo.HandlerType.Assembly)
                && CorsAttribute.Nearest(declarations.Declared) is { } declared
            )
            {
                return declared;
            }
        }

        return null;
    }
}
