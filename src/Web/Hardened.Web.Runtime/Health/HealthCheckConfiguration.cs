namespace Hardened.Web.Runtime.Health;

/// <summary>
/// Where the health endpoints live and how long they may take.
/// </summary>
public class HealthCheckConfiguration {

    /// <summary>
    /// "Is this process wedged?" Runs no checks and answers 200 if the pipeline runs at all.
    /// </summary>
    /// <remarks>
    /// Deliberately answers without consulting a single dependency. An orchestrator restarts what
    /// fails liveness, so a liveness probe that failed during a database outage would restart every
    /// replica at once and turn a recoverable dependency failure into a total one.
    /// </remarks>
    public string LivePath { get; set; } = "/health/live";

    /// <summary>
    /// "Should traffic come here?" Runs the registered checks. Failing takes this instance out of
    /// the load balancer and leaves it running.
    /// </summary>
    public string ReadyPath { get; set; } = "/health/ready";

    /// <summary>
    /// How long one check may take before it counts as unhealthy.
    /// </summary>
    /// <remarks>
    /// A health endpoint that hangs is worse than one that fails: the orchestrator's own probe
    /// timeout decides instead, and it usually decides to restart. Bounding it here is what keeps
    /// the answer the application's to give.
    /// </remarks>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the whole readiness probe may take, however many checks are registered.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Report each check by name and status in the response body.
    /// </summary>
    /// <remarks>
    /// Off. Readiness is unauthenticated by construction, and a body enumerating dependency names
    /// and error strings tells an anonymous caller about the inside of the system. Turn it on
    /// behind a private listener, or when the endpoint is not routable from outside.
    /// </remarks>
    public bool IncludeDetail { get; set; }
}
