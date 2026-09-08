namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// The handlers an application answers as <c>text/event-stream</c>, known at startup.
/// </summary>
/// <remarks>
/// <para>
/// Written by the routing generator, which is the only place the answer is available before a
/// request arrives. A routing table builds its handlers lazily, so enumerating them to ask would
/// mean constructing every filter chain in the application at startup to read one flag off each.
/// A table with no such handler emits no manifest at all.
/// </para>
/// <para>
/// <b>It exists for the hosts whose framing depends on their deployment.</b> Kestrel and ASP.NET
/// Core flush whatever they are given, so nothing there has a question to ask. A Lambda's response
/// mode is an environment variable, so the same assembly serves both and the build cannot refuse
/// the combination - which leaves the application itself as the only thing that can say the two
/// disagree. See <c>Hardened.Aws.Lambda.Runtime</c>'s
/// <c>ServerSentEventsResponseModeStartupService</c>.
/// </para>
/// <para>
/// Here rather than beside the routing table because <c>Hardened.Aws.Lambda.Runtime</c> references
/// no web package, deliberately: an adapter produces an <c>IExecutionRequest</c> and the pipeline
/// routes it. Which handlers are framed as events is a pipeline fact, not a routing one.
/// </para>
/// </remarks>
public interface IServerSentEventManifest {
    /// <summary>
    /// One entry per handler, as the verb and path an operator would recognise from a log line -
    /// <c>GET /orders/live</c>.
    /// </summary>
    IReadOnlyList<string> Handlers { get; }
}
