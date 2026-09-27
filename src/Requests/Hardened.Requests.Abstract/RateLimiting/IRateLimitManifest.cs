namespace Hardened.Requests.Abstract.RateLimiting;

/// <summary>
/// The handlers an application declares a rate limit on, known at startup.
/// </summary>
/// <remarks>
/// <para>
/// Written by the routing generator for the reason
/// <see cref="Serializer.IServerSentEventManifest"/> is: a routing table builds its handlers
/// lazily, so asking them at startup would mean constructing every filter chain in the
/// application. A table with no rate limit emits no manifest at all.
/// </para>
/// <para>
/// Read by the AWS Lambda runtime. The in-process store counts per execution environment, and
/// Lambda runs as many environments as traffic needs, so a limit counted there is not one.
/// </para>
/// </remarks>
public interface IRateLimitManifest
{
    /// <summary>
    /// One entry per handler, as the verb and path an operator would recognise from a log line -
    /// <c>GET /quotes</c>.
    /// </summary>
    IReadOnlyList<string> Handlers { get; }
}
