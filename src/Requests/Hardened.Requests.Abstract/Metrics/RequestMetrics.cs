using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Abstract.Metrics;

public static class RequestMetrics {
    public static readonly IMetricDefinition TotalRequestDuration =
        new MetricDefinition("TotalRequestDuration", MetricUnits.Milliseconds);

    public static readonly IMetricDefinition ResponseDuration =
        new MetricDefinition("ResponseDuration", MetricUnits.Milliseconds);

    public static readonly IMetricDefinition ParameterBindDuration =
        new MetricDefinition("ParameterBindDuration", MetricUnits.Milliseconds);

    public static readonly IMetricDefinition HandlerInvokeDuration =
        new MetricDefinition("HandlerDuration", MetricUnits.Milliseconds);

    /// <summary>
    /// One per request whose deadline ran out, and nothing at all for a request that finished in
    /// time.
    /// </summary>
    /// <remarks>
    /// Counted only where the budget was what expired. A client that hangs up cancels the same
    /// linked token, and counting that here would report the slow handler this metric exists to
    /// find every time somebody closed a tab.
    /// </remarks>
    public static readonly IMetricDefinition RequestTimedOut =
        new MetricDefinition("RequestTimedOut", MetricUnits.Count);
}