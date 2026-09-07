namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// Delivers messages to a trigger route, as a test harness does.
/// </summary>
/// <remarks>
/// <para>
/// A named delegate rather than a <c>Func</c> of the same shape, and identity is the whole reason.
/// A test harness constructs a generated façade by finding a constructor that takes this, and a
/// structural match on <c>Func&lt;object, string, string, Task&gt;</c> would also match anything
/// else that happened to have that shape - which would hand somebody else's type a delegate it
/// never asked for and fail somewhere strange. Nothing has a constructor taking this by accident.
/// </para>
/// <para>
/// <b>Here rather than in a testing package on purpose.</b> The façade is generated into the
/// application, so whatever its constructor names becomes an application dependency. Every Hardened
/// application already references this package - it is where the handler attributes live - so
/// naming this costs nothing, where naming a testing type would put test scaffolding in a published
/// build's dependency graph.
/// </para>
/// </remarks>
/// <param name="messages">
/// The batch. Always a collection, because every trigger source that carries one message can carry
/// ten and the fan-out is what a handler runs behind.
/// </param>
public delegate Task TriggerSend(object messages, string scheme, string path);

/// <summary>
/// Invokes an operation and returns what it answered.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="TriggerSend"/> for a direct invocation, which is the one family
/// with a caller waiting. <paramref name="responseType"/> is what the caller expects back, or null
/// for a handler that returns nothing; how a harness produces it differs by how it delivered.
/// </remarks>
public delegate Task<object?> TriggerCall(
    object message, string scheme, string path, Type? responseType);
