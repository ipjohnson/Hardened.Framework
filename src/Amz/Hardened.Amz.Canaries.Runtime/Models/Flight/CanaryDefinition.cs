namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public record CanaryDefinition(
    string TestClassName,
    string TestMethod,
    CanaryFrequency Frequency,
    bool ReportMetrics);