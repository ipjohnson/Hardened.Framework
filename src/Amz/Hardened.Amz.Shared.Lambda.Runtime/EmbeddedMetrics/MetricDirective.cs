using System.Text.Json.Serialization;

namespace Hardened.Amz.Shared.Lambda.Runtime.EmbeddedMetrics;

public enum EmbeddedMetricUnit
{
    None,
    Seconds,
    Milliseconds,
    Count
}

public record EmbeddedMetricMetadata(
    long Timestamp,
    IReadOnlyList<EmbeddedMetricDirective> CloudWatchMetrics);

public record EmbeddedMetricDirective(
    string Namespace, 
    IReadOnlyList<IReadOnlyList<string>> Dimensions,
    IReadOnlyList<EmbeddedMetricDefinition> Metrics);

public record EmbeddedMetricDefinition(string Name, EmbeddedMetricUnit Unit);
    