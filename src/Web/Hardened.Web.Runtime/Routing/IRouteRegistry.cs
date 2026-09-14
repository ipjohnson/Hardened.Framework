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

    /// <summary>
    /// Serves <paramref name="handler"/> at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lambda form. The generator reads the lambda at the call site - its parameters, its
    /// return type and any attribute written on it - emits a handler, a binder and a factory from
    /// them, and rewrites this call to pass the factory. So the handler is compiled code and the
    /// path is the only part that comes from run time, which is the same split the controller form
    /// makes.
    /// </para>
    /// <para>
    /// <b>These throw when they run.</b> The generated overload is the only one meant to execute; a
    /// call reaching this one is a call the generator did not see, and doing something reasonable
    /// instead would be a route that silently binds nothing.
    /// </para>
    /// </remarks>
    IRouteRegistry Map(string method, string path, Delegate handler);

    IRouteRegistry Get(string path, Delegate handler);

    IRouteRegistry Post(string path, Delegate handler);

    IRouteRegistry Put(string path, Delegate handler);

    IRouteRegistry Patch(string path, Delegate handler);

    IRouteRegistry Delete(string path, Delegate handler);

    /// <summary>
    /// Serves a handler the generator emitted. Called by generated code.
    /// </summary>
    IRouteRegistry Map(string path, RegisteredRouteHandler handler);
}
