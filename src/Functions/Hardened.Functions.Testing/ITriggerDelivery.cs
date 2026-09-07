namespace Hardened.Functions.Testing;

/// <summary>
/// How a test message reaches the function.
/// </summary>
/// <remarks>
/// <para>
/// The seam between the two fidelities. <c>PipelineDelivery</c> builds a request and runs the
/// pipeline, which covers routing, the batch fan-out, binding and the handler, and names no cloud.
/// A provider's testing package replaces it with one that builds the envelope that provider
/// actually sends and goes in through its host, which adds the adapter, the peek, the body encoding
/// that source uses and the metadata it carries.
/// </para>
/// <para>
/// A test method reads the same either way - the façade and the <c>Func</c> it holds do not change -
/// so a project chooses its fidelity with an assembly attribute and nothing else moves.
/// </para>
/// <para>
/// Internal to the harness rather than something generated code touches. The façade takes a
/// <c>Func</c> of BCL types precisely so an application carrying one references no testing package;
/// that constraint applies to the façade, not to what sits behind it.
/// </para>
/// </remarks>
public interface ITriggerDelivery {
    /// <param name="messages">
    /// Always a collection, because every trigger source that carries one message can carry ten and
    /// the fan-out is what a handler runs behind.
    /// </param>
    Task Deliver(IReadOnlyList<object> messages, string scheme, string path);

    /// <summary>
    /// One invocation, and what it answered.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Deliver"/> because a batch has no single answer to return, and a
    /// direct invocation is never a batch. <paramref name="responseType"/> is what the caller
    /// expects back, or null for a handler that returns nothing; how a delivery produces it differs
    /// - through the pipeline it is the object the handler returned, through an envelope it is
    /// bytes to be read with the framework's conventions.
    /// </remarks>
    Task<object?> Call(object message, string scheme, string path, Type? responseType);
}
