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
        RouteHandlerFactory factory,
        string operation = ""
    )
    {
        Method = method;
        BoundTokens = boundTokens;
        Factory = factory;
        Operation = operation;
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

    /// <summary>
    /// What this route publishes, as the operation object of an OpenAPI path item -
    /// <c>"get":{…}</c>.
    /// </summary>
    /// <remarks>
    /// Written by the build, because everything in it is known there: the parameters, their types
    /// and constraints, the request body, every declared response. The path is the only hole, and
    /// the path is what the registration supplies - so the served document is assembled by writing
    /// this between the two halves of the compiled one. No JSON is parsed and no schema is written
    /// at run time.
    ///
    /// <para>
    /// Empty where the application serves no document, which is what an entry point without
    /// <c>[OpenApiDocument]</c> gets.
    /// </para>
    /// </remarks>
    public string Operation { get; }
}
