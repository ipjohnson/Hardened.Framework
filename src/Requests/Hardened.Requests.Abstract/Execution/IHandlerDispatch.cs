namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// The last filter in a chain: the one that finds the handler a request routes to and runs it.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one per application, and which one is a property of the handlers rather than
/// of the host. Web verbs compile to a routing table behind <c>IWebExecutionHandlerService</c>;
/// triggers and <c>[HardenedFunction]</c> compile to a name switch behind
/// <c>IFunctionHandlerProvider</c>. Both are dispatch, and a host should install whichever the
/// application declared without knowing which that is.
/// </para>
/// <para>
/// That is what this exists for. A Lambda function serving HTTP and one serving a queue are the
/// same host with different handlers, and the host references neither the web package nor the
/// function one - it asks the container what was registered.
/// </para>
/// </remarks>
public interface IHandlerDispatch : IExecutionFilter { }
