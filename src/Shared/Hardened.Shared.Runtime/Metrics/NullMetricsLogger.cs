namespace Hardened.Shared.Runtime.Metrics;

public class NullMetricsLogger : IMetricLogger
{
    public void Dispose()
    {
            
    }

    public Task Flush()
    {
        return Task.CompletedTask;
    }

    public void Record(IMetricDefinition metric, double value)
    {

    }

    public void Tag(string tagName, object tagValue)
    {

    }

    public void Data(string dataName, object dataValue)
    {

    }
}