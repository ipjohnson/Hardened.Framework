namespace Hardened.Shared.Runtime.Metrics;

public interface IMetricLogger : IDisposable {
    Task Flush();

    void Record(IMetricDefinition metric, double value);

    void Tag(string tagName, object tagValue);

    void Data(string dataName, object dataValue);
}