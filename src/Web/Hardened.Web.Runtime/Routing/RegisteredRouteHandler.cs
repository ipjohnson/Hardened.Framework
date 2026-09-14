namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// A handler the generator emitted for one registration call site.
/// </summary>
/// <remarks>
/// <para>
/// The lambda form's counterpart to <see cref="GeneratedRouteHandler"/>. A lambda has no controller
/// and no declared route, so it carries the one thing the registry needs to check and cannot
/// otherwise know: the tokens its binder reads out of the path.
/// </para>
/// <para>
/// Constructed only by generated code. Nothing an application writes has the factory to put in it.
/// </para>
/// </remarks>
public sealed class RegisteredRouteHandler
{
    public RegisteredRouteHandler(
        string method,
        IReadOnlyList<string> boundTokens,
        RouteHandlerFactory factory
    )
    {
        Method = method;
        BoundTokens = boundTokens;
        Factory = factory;
    }

    public string Method { get; }

    /// <summary>
    /// The path tokens the handler's binder reads, by name.
    /// </summary>
    /// <remarks>
    /// A template that does not declare one of these binds nothing for it, and the handler answers
    /// 400 to every request. That is a startup failure rather than something to find in production.
    /// </remarks>
    public IReadOnlyList<string> BoundTokens { get; }

    public RouteHandlerFactory Factory { get; }
}
