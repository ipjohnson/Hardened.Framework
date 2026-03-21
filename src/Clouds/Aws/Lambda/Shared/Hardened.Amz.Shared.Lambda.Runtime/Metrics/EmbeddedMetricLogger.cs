using System.Text.Json;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Shared.Lambda.Runtime.Metrics;

public class EmbeddedMetricLogger : IMetricLogger {
    private int _disposed;
    private readonly string _namespace;
    private readonly IDimensionSetProvider _dimensionSetProvider;
    private readonly List<Tuple<string, object>> _tags = new();
    private readonly Dictionary<string, object> _properties = new();
    private readonly List<(string Name, double Value, string Unit)> _metrics = new();

    public EmbeddedMetricLogger(string loggerName, IDimensionSetProvider dimensionSetProvider) {
        _namespace = loggerName;
        _dimensionSetProvider = dimensionSetProvider;
    }

    public void Dispose() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
            WriteEmfLog();
        }
    }

    public Task Flush() {
        WriteEmfLog();
        return Task.CompletedTask;
    }

    public void Record(IMetricDefinition metric, double value) {
        var unit = metric.Units.Name switch {
            "Milliseconds" => "Milliseconds",
            "Seconds" => "Seconds",
            _ => "Count"
        };

        _metrics.Add((metric.Name, value, unit));
    }

    public void Tag(string tagName, object tagValue) {
        _tags.Add(new Tuple<string, object>(tagName, tagValue));
    }

    public void Data(string dataName, object dataValue) {
        _properties[dataName] = dataValue;
    }

    private void WriteEmfLog() {
        if (_metrics.Count == 0) return;

        var dimensionSets = _dimensionSetProvider.Get(_tags).ToList();

        var logEntry = new Dictionary<string, object>();

        // Add _aws EMF header
        logEntry["_aws"] = new {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CloudWatchMetrics = new[] {
                new {
                    Namespace = _namespace,
                    Dimensions = dimensionSets.Select(d => d.ToArray()).ToArray(),
                    Metrics = _metrics.Select(m => new { Name = m.Name, Unit = m.Unit }).ToArray()
                }
            }
        };

        // Add dimension values
        foreach (var tag in _tags) {
            logEntry[tag.Item1] = tag.Item2;
        }

        // Add metric values
        foreach (var (name, value, _) in _metrics) {
            logEntry[name] = value;
        }

        // Add properties
        foreach (var (key, value) in _properties) {
            logEntry[key] = value;
        }

        Console.WriteLine(JsonSerializer.Serialize(logEntry));
        _metrics.Clear();
    }
}
