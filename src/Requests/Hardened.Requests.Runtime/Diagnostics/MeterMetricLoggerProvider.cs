using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Runtime.Diagnostics;

/// <summary>
/// Records what the pipeline measures onto <see cref="HardenedDiagnostics.Meter"/>, so that anything
/// subscribing to it — an OpenTelemetry SDK, prometheus-net, <c>dotnet-counters</c> — sees the same
/// numbers CloudWatch does.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not registered. <c>NullMetricLoggerProvider</c> is the framework default and
/// registers with <c>RegistrationType.Try</c>; the Lambda EMF provider registers unconditionally and
/// so beats it. A third unconditional registration would mean whichever module happened to be
/// composed last decided where an application's metrics went. An application asks for this one:
/// </para>
/// <code>
/// services.RemoveAll&lt;IMetricLoggerProvider&gt;();
/// services.AddSingleton&lt;IMetricLoggerProvider, MeterMetricLoggerProvider&gt;();
/// </code>
/// <para>
/// <c>loggerName</c> is ignored. EMF uses it as a CloudWatch namespace; a <c>Meter</c> already has a
/// name, and the scope a consumer subscribes to is <see cref="HardenedDiagnostics.SourceName"/>.
/// Turning a per-request string into a per-request Meter would create one instrument set per request.
/// </para>
/// </remarks>
public class MeterMetricLoggerProvider : IMetricLoggerProvider {
    /// <summary>
    /// One instrument per metric name, for the life of the process.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateLogger"/> is called once per request, and creating a histogram per request
    /// would publish a fresh instrument to every listener each time. The instruments belong to the
    /// static Meter, so they are cached statically alongside it rather than per provider instance.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    /// <summary>
    /// Metrics the HTTP semantic conventions already have a name for, and the factor that converts
    /// what Hardened records into what the conventions expect.
    /// </summary>
    /// <remarks>
    /// The conventions define <c>http.server.request.duration</c> in <em>seconds</em>;
    /// <c>RequestMetrics</c> is in milliseconds. The translation happens here rather than by
    /// changing the definition, because the definition is also what EMF emits — renaming it or
    /// changing its unit would move every existing CloudWatch dashboard underneath a spec those
    /// dashboards do not care about.
    ///
    /// Everything not named here passes through as it is, including an application's own metrics.
    /// There is no convention for how long a handler took to bind its parameters.
    /// </remarks>
    private static readonly Dictionary<string, (string Name, string Unit, double Scale)> _conventions =
        new() {
            ["TotalRequestDuration"] = ("http.server.request.duration", "s", 0.001)
        };

    public IMetricLogger CreateLogger(string loggerName) {
        return new MeterMetricLogger();
    }

    private static Histogram<double> HistogramFor(IMetricDefinition metric) {
        return _histograms.GetOrAdd(
            metric.Name,
            static (_, definition) => {
                var (name, unit, _) = Translate(definition);

                return HardenedDiagnostics.Meter.CreateHistogram<double>(name, unit);
            },
            metric);
    }

    private static (string Name, string Unit, double Scale) Translate(IMetricDefinition metric) {
        return _conventions.TryGetValue(metric.Name, out var convention)
            ? convention
            : (metric.Name, Ucum(metric.Units), 1d);
    }

    /// <summary>
    /// <see cref="MetricUnits"/> spells its units the way CloudWatch does. A Meter unit is UCUM.
    /// </summary>
    private static string Ucum(MetricUnits units) {
        return units.Name switch {
            "Milliseconds" => "ms",
            "Seconds" => "s",
            "Count" => "{count}",
            _ => units.Name
        };
    }

    /// <summary>
    /// Buffers a request's measurements and records them when it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Buffered rather than recorded as they arrive, because a histogram wants its dimensions at the
    /// moment of recording and the pipeline does not have them yet. Every host records
    /// <c>TotalRequestDuration</c> and only then calls <c>RequestEnd</c>, so anything tagged with the
    /// status or the route would arrive after the measurement it belongs to. Buffering also means
    /// <see cref="IMetricLogger"/> behaves the same way whichever provider is behind it — EMF has
    /// always accumulated and written one line at the end.
    /// </para>
    /// <para>
    /// The flush point exists because every host disposes its logger. That was not true until
    /// recently: three of them recorded and never disposed, which is invisible under the null
    /// provider and would have made this one silently emit nothing.
    /// </para>
    /// </remarks>
    private sealed class MeterMetricLogger : IMetricLogger {
        private readonly List<(IMetricDefinition Metric, double Value)> _measurements = [];
        private readonly List<KeyValuePair<string, object?>> _tags = [];
        private int _disposed;

        public void Record(IMetricDefinition metric, double value) {
            _measurements.Add((metric, value));
        }

        public void Tag(string tagName, object tagValue) {
            _tags.Add(new KeyValuePair<string, object?>(tagName, tagValue));
        }

        /// <summary>
        /// Dropped. A Meter measurement carries dimensions, not arbitrary properties, and promoting
        /// free-form data to dimensions is how a metrics bill becomes a surprise.
        /// </summary>
        public void Data(string dataName, object dataValue) { }

        public Task Flush() {
            Emit();

            return Task.CompletedTask;
        }

        public void Dispose() {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
                Emit();
            }
        }

        private void Emit() {
            if (_measurements.Count == 0) {
                return;
            }

            var tags = _tags.ToArray();

            foreach (var (metric, value) in _measurements) {
                var (_, _, scale) = Translate(metric);

                HistogramFor(metric).Record(value * scale, tags);
            }

            _measurements.Clear();
        }
    }
}
