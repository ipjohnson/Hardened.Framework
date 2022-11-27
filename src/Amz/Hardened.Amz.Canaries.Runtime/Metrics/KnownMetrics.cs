using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Canaries.Runtime.Metrics;

public static class KnownMetrics
{
    public static IMetricDefinition Success = new MetricDefinition("Success", MetricUnits.Count);

    public static IMetricDefinition Duration = new MetricDefinition("Duration", MetricUnits.Milliseconds);
}