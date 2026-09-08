namespace Hardened.Azure.Functions.Runtime.Execution;

/// <summary>
/// Which dispatch an invocation runs through, decided when the shim was generated.
/// </summary>
/// <remarks>
/// On Lambda a function serves one family, so the invocation loop installs the one
/// <c>IHandlerDispatch</c> an application registered and refuses an application that registered
/// two. On Azure one worker hosts every function of the application, and an application with
/// <c>[Get]</c> routes beside a <c>[Queue]</c> handler is the ordinary case rather than a
/// contradiction. The generator knows whether a shim is the HTTP catch-all or a trigger, so it says
/// which here and the invocation handler selects the dispatch per invocation.
/// </remarks>
public enum FunctionsDispatch {
    /// <summary>
    /// A trigger: the name switch behind <c>IFunctionHandlerProvider</c>, which is
    /// <c>FunctionDispatchFilter</c>.
    /// </summary>
    Trigger,

    /// <summary>
    /// The HTTP catch-all: the routing table behind <c>IWebExecutionHandlerService</c>, which
    /// the HTTP adapter package registers.
    /// </summary>
    Web
}

/// <summary>
/// What one generated shim hands the invocation handler: the route it was generated for, and what
/// the worker bound for it.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>LambdaPayload</c>, and simpler for a reason: Lambda hands a function
/// bytes and the adapters have to recognise them, while the Functions host hands a function the
/// typed value its binding attribute asked for and has already decided which function that is. The
/// shim generated for <c>[Queue("orders")]</c> is <c>Queue_orders</c>, is invoked for nothing
/// else, and knows its route at generation time. So there is no peek and no parse: the function
/// identity is the route.
/// </para>
/// <para>
/// <see cref="Data"/> is typed by the shim's parameter, which the generator chose for the adapter
/// that serves the trigger - <c>ServiceBusReceivedMessage[]</c> for a queue. An adapter recognises
/// its own type in <c>ITriggerAdapter.Handles</c>, which is the whole of what recognition means
/// here.
/// </para>
/// </remarks>
public sealed class FunctionsTrigger {
    public FunctionsTrigger(
        string scheme, string path, object data, FunctionsDispatch dispatch = FunctionsDispatch.Trigger) {
        Scheme = scheme;
        Path = path;
        Data = data;
        Dispatch = dispatch;
    }

    /// <summary>The scheme the shim routes under: <c>QUEUE</c>, <c>TOPIC</c>, <c>TIMER</c>.</summary>
    public string Scheme { get; }

    /// <summary>The source's own name as a rooted path: <c>/orders</c>.</summary>
    public string Path { get; }

    /// <summary>What the worker bound for the shim's trigger parameter.</summary>
    public object Data { get; }

    /// <summary>Which dispatch the invocation runs through.</summary>
    public FunctionsDispatch Dispatch { get; }
}
