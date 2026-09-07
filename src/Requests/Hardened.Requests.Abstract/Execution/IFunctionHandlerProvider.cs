namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// The handlers a function application compiled, looked up by route.
/// </summary>
/// <remarks>
/// The function counterpart of a web routing table, and keyed the same way: on the scheme and the
/// path together. A queue called <c>orders</c> and a topic called <c>orders</c> are two sources, not
/// one, so keying on the path alone made them collide - and because the two handlers then wanted
/// the same generated file name, the generator threw and every handler in the project disappeared
/// behind one message about a duplicate hint name.
/// </remarks>
public interface IFunctionHandlerProvider {
    /// <param name="scheme">
    /// What the adapter put on the request: <c>QUEUE</c>, <c>TOPIC</c>, <c>TIMER</c>, <c>EVENT</c>,
    /// <c>INVOKE</c>. Neutral rather than named after the service that delivered, so the routing
    /// table a handler compiles to is the same whichever provider serves it.
    /// </param>
    /// <param name="path">The source's own name, as a rooted path.</param>
    IExecutionRequestHandler? GetFunctionHandler(
        string scheme, string path, IServiceProvider serviceProvider);
}
