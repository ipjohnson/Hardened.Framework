namespace Hardened.Shared.Runtime.Metrics;

public class NullMetricLoggerProvider : IMetricLoggerProvider {
    private static readonly IMetricLogger _logger = new NullMetricsLogger();

    public IMetricLogger CreateLogger(string loggerName) {
        return _logger;
    }
}