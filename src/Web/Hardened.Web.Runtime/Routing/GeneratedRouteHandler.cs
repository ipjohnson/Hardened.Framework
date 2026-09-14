using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Builds one generated handler, served at <paramref name="routePath"/>.
/// </summary>
/// <remarks>
/// The path is an argument because a generated handler takes one: the same class is constructed
/// once per path it is registered at, and each instance reports its own path through
/// <c>IExecutionRequestHandlerInfo</c>. Filters, conventions, authorization requirements and link
/// resolution all read it from there.
/// </remarks>
public delegate IExecutionRequestHandler RouteHandlerFactory(
    IServiceProvider serviceProvider,
    string? routePath
);

/// <summary>
/// One handler the generator emitted, and the attribute route it was emitted from.
/// </summary>
public sealed class GeneratedRouteHandler
{
    public GeneratedRouteHandler(
        Type controllerType,
        string handlerMethod,
        string method,
        string declaredPath,
        RouteHandlerFactory factory
    )
    {
        ControllerType = controllerType;
        HandlerMethod = handlerMethod;
        Method = method;
        DeclaredPath = declaredPath;
        Factory = factory;
    }

    public Type ControllerType { get; }

    /// <summary>The name of the method on <see cref="ControllerType"/> that answers.</summary>
    public string HandlerMethod { get; }

    public string Method { get; }

    /// <summary>
    /// The route the handler was declared with.
    /// </summary>
    /// <remarks>
    /// Read at registration to check the token names. A handler compiled to bind <c>id</c> and
    /// registered under a template that declares <c>orderId</c> would bind nothing, and answer 400
    /// to every request, which is the failure this exists to turn into a startup error.
    /// </remarks>
    public string DeclaredPath { get; }

    public RouteHandlerFactory Factory { get; }
}

/// <summary>
/// Every handler the generator emitted for one entry point.
/// </summary>
/// <remarks>
/// Emitted beside the routing table, and only where the compilation declares an
/// <see cref="IRouteRegistration"/>. An application that registers no routes at run time generates
/// exactly what it generated before this existed.
/// </remarks>
public interface IGeneratedRouteHandlerCatalog
{
    IReadOnlyList<GeneratedRouteHandler> Handlers { get; }

    /// <summary>
    /// Whether this entry point matches without regard to case, from
    /// <c>[CaseInsensitiveRoutes]</c>.
    /// </summary>
    /// <remarks>
    /// The attribute is a build-time switch, because the generated matcher is compiled and there is
    /// nothing left at run time to decide. A registered route has to behave the same way, so the
    /// flag is carried here rather than read again from anywhere else.
    /// </remarks>
    bool CaseInsensitiveRoutes { get; }

    /// <summary>The entry point's <c>[BasePath]</c>, or an empty string.</summary>
    /// <remarks>
    /// Composed onto a registered path exactly as it is composed onto an attribute route. A module
    /// mounted at <c>/catalog</c> serves everything it declares under it, however the declaration
    /// was written.
    /// </remarks>
    string BasePath { get; }

    /// <summary>
    /// The <c>[RouteConstraint]</c> methods the application declared, by the name a template uses
    /// after the colon.
    /// </summary>
    /// <remarks>
    /// A constraint has to be known at compile time to be compiled in, and it still is: this is the
    /// same static method the generated table calls directly, handed over as a delegate so a
    /// template that only exists at run time can name it.
    /// </remarks>
    IReadOnlyDictionary<string, RouteConstraintTest> Constraints { get; }
}
