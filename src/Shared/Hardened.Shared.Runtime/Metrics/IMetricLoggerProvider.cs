namespace Hardened.Shared.Runtime.Metrics;

public interface IMetricLoggerProvider {
    IMetricLogger CreateLogger(string loggerName);
}