namespace Hardened.Web.Runtime.Health;

/// <summary>
/// How healthy something the application depends on is.
/// </summary>
public enum HealthStatus {
    /// <summary>Working.</summary>
    Healthy = 0,

    /// <summary>
    /// Working, worse than it should be. Answers 200: a warm-but-not-hot cache or a slow replica is
    /// not a reason to take an instance out of the load balancer.
    /// </summary>
    Degraded = 1,

    /// <summary>Not working. Answers 503 on readiness.</summary>
    Unhealthy = 2
}

/// <summary>
/// The verdict from one check.
/// </summary>
/// <param name="Status">How healthy.</param>
/// <param name="Description">
/// Why, for a human reading a log. Only reaches the response body when the endpoint is configured
/// to report detail, because readiness is unauthenticated.
/// </param>
public readonly record struct HealthCheckResult(HealthStatus Status, string? Description = null) {
    public static HealthCheckResult Healthy(string? description = null) =>
        new(HealthStatus.Healthy, description);

    public static HealthCheckResult Degraded(string? description = null) =>
        new(HealthStatus.Degraded, description);

    public static HealthCheckResult Unhealthy(string? description = null) =>
        new(HealthStatus.Unhealthy, description);
}

/// <summary>
/// Something the application needs, which readiness asks about.
/// </summary>
/// <remarks>
/// <para>
/// Register with <c>TryAddEnumerable</c> semantics - <c>[SingletonService(Using =
/// RegistrationType.TryEnumerable)]</c> - not <c>Try</c>. This is resolved as a collection, and a
/// plain <c>Try</c> would mean the second check registered silently never runs.
/// </para>
/// <para>
/// A check is called on every readiness probe, which an orchestrator issues every few seconds. It
/// should be cheap and it must not be the real work: "can I reach the database" is a connection
/// test, not a query. It is also called with a token that will be cancelled - see
/// <c>HealthCheckConfiguration.CheckTimeout</c> - and is expected to honour it.
/// </para>
/// </remarks>
public interface IHealthCheck {
    /// <summary>
    /// What this checks, as it appears in the detailed response. Should be stable and short.
    /// </summary>
    string Name { get; }

    Task<HealthCheckResult> Check(CancellationToken cancellationToken);
}
