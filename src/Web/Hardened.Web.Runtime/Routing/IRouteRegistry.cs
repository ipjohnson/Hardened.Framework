namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// What an <see cref="IRouteRegistration"/> registers routes through.
/// </summary>
/// <remarks>
/// <para>
/// The template language, the constraint names and the token rules are the attribute form's,
/// unchanged. There is one routing syntax, so there is one thing to document and one thing to fix.
/// </para>
/// <para>
/// Every call is checked and every failure is collected. Registration throws once, at the end, with
/// all of them - a registry that threw on the first bad route would make a fifty-route registration
/// a fifty-restart debugging session.
/// </para>
/// </remarks>
public interface IRouteRegistry
{
    /// <summary>
    /// The container the application was built from, for a dependency that cannot be taken through
    /// a constructor.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Serves the handler <paramref name="handlerMethod"/> on <paramref name="controllerType"/> at
    /// <paramref name="path"/>, under <paramref name="method"/>.
    /// </summary>
    /// <remarks>
    /// The verb has to be the one the handler was declared with. A handler carries its verb in the
    /// information every filter reads, so serving a <c>[Get]</c> handler under POST would leave the
    /// two disagreeing.
    /// </remarks>
    IRouteRegistry Map(string method, string path, Type controllerType, string handlerMethod);

    IRouteRegistry Get(string path, Type controllerType, string handlerMethod);

    IRouteRegistry Post(string path, Type controllerType, string handlerMethod);

    IRouteRegistry Put(string path, Type controllerType, string handlerMethod);

    IRouteRegistry Patch(string path, Type controllerType, string handlerMethod);

    IRouteRegistry Delete(string path, Type controllerType, string handlerMethod);
}
