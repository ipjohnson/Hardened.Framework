using System.Diagnostics.Metrics;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Diagnostics;

/// <summary>
/// What the pipeline's measurements look like once they reach a <c>Meter</c>.
/// </summary>
/// <remarks>
/// Instrument names and units are literals here rather than constants shared with the code under
/// test — they are what a dashboard query is written against, so a rename has to fail a test rather
/// than pass one.
/// </remarks>
[Collection(DiagnosticsListenerCollection.Name)]
public class MeterMetricLoggerProviderTests {

    private sealed record Measurement(string Instrument, string? Unit, double Value, KeyValuePair<string, object?>[] Tags);

    /// <summary>
    /// Collects measurements published by the Hardened meter while it is alive.
    /// </summary>
    private sealed class Listening : IDisposable {
        private readonly MeterListener _listener;

        public Listening() {
            _listener = new MeterListener {
                InstrumentPublished = (instrument, listener) => {
                    if (instrument.Meter.Name == "Hardened.Requests") {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Measurements.Add(new Measurement(instrument.Name, instrument.Unit, value, tags.ToArray())));

            _listener.Start();
        }

        public List<Measurement> Measurements { get; } = [];

        public Measurement Single(string instrument) =>
            Assert.Single(Measurements, m => m.Instrument == instrument);

        public void Dispose() => _listener.Dispose();
    }

    private static IMetricLogger Logger() => new MeterMetricLoggerProvider().CreateLogger("ignored");

    /// <summary>
    /// The conventions name this one and define it in seconds; <c>RequestMetrics</c> records
    /// milliseconds. Translating here rather than changing the definition keeps every existing EMF
    /// dashboard pointing at the same numbers it always did.
    /// </summary>
    [Fact]
    public void TheRequestDurationTakesItsConventionalNameAndUnit() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(RequestMetrics.TotalRequestDuration, 1500);
        logger.Dispose();

        var measurement = listening.Single("http.server.request.duration");

        Assert.Equal("s", measurement.Unit);
        Assert.Equal(1.5, measurement.Value, 6);
    }

    /// <summary>
    /// There is no convention for how long parameter binding took, so it passes through under its
    /// own name — as does anything an application defines for itself.
    /// </summary>
    [Fact]
    public void AMetricWithNoConventionPassesThroughUnchanged() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(RequestMetrics.ParameterBindDuration, 12);
        logger.Dispose();

        var measurement = listening.Single("ParameterBindDuration");

        Assert.Equal("ms", measurement.Unit);
        Assert.Equal(12, measurement.Value);
    }

    [Fact]
    public void UnitsAreTranslatedToUcum() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(new MetricDefinition("ucum.probe.count", MetricUnits.Count), 3);
        logger.Record(new MetricDefinition("ucum.probe.seconds", MetricUnits.Seconds), 4);
        logger.Dispose();

        Assert.Equal("{count}", listening.Single("ucum.probe.count").Unit);
        Assert.Equal("s", listening.Single("ucum.probe.seconds").Unit);
    }

    /// <summary>
    /// The reason measurements are buffered rather than recorded as they arrive. Every host records
    /// the request duration and only then calls <c>RequestEnd</c>, so a dimension attached at the end
    /// of a request would otherwise arrive after the measurement it describes and be lost.
    /// </summary>
    [Fact]
    public void AMeasurementCarriesTagsSetAfterItWasRecorded() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(RequestMetrics.TotalRequestDuration, 1000);
        logger.Tag("http.route", "/orders/{id}");
        logger.Dispose();

        var tag = Assert.Single(listening.Single("http.server.request.duration").Tags);

        Assert.Equal("http.route", tag.Key);
        Assert.Equal("/orders/{id}", tag.Value);
    }

    [Fact]
    public void NothingIsRecordedUntilTheRequestEnds() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(new MetricDefinition("buffering.probe", MetricUnits.Count), 1);

        Assert.DoesNotContain(listening.Measurements, m => m.Instrument == "buffering.probe");

        logger.Dispose();

        Assert.Contains(listening.Measurements, m => m.Instrument == "buffering.probe");
    }

    /// <summary>
    /// A host that flushes and then disposes — or disposes twice — records the request once.
    /// </summary>
    [Fact]
    public async Task FlushingAndThenDisposingRecordsOnce() {
        using var listening = new Listening();

        var logger = Logger();
        logger.Record(new MetricDefinition("once.probe", MetricUnits.Count), 1);

        await logger.Flush();
        logger.Dispose();
        logger.Dispose();

        Assert.Single(listening.Measurements, m => m.Instrument == "once.probe");
    }

    /// <summary>
    /// <c>CreateLogger</c> runs once per request. A histogram created per request would publish a
    /// fresh instrument to every listener on every request, so the instruments are cached against
    /// the process-lifetime meter instead.
    /// </summary>
    [Fact]
    public void TwoRequestsShareOneInstrument() {
        var published = new List<Instrument>();

        using var listener = new MeterListener {
            InstrumentPublished = (instrument, _) => {
                if (instrument.Name == "sharing.probe") {
                    published.Add(instrument);
                }
            }
        };

        listener.Start();

        var provider = new MeterMetricLoggerProvider();
        var definition = new MetricDefinition("sharing.probe", MetricUnits.Count);

        var first = provider.CreateLogger("a");
        first.Record(definition, 1);
        first.Dispose();

        var second = provider.CreateLogger("b");
        second.Record(definition, 2);
        second.Dispose();

        Assert.Single(published);
    }
}
