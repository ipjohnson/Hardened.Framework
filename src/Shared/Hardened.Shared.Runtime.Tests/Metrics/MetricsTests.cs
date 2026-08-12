using Hardened.Shared.Runtime.Metrics;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Metrics;

/// <summary>
/// The metrics surface, and the null implementation registered when nothing else is. Every
/// framework path records metrics unconditionally, so the null logger is on the request path of
/// every application that never configured one.
/// </summary>
public class MetricsTests {

    [Fact]
    public void AMetricDefinitionCarriesItsNameAndUnits() {
        var definition = new MetricDefinition("RequestDuration", MetricUnits.Milliseconds);

        Assert.Equal("RequestDuration", definition.Name);
        Assert.Same(MetricUnits.Milliseconds, definition.Units);
    }

    [Fact]
    public void AMetricDefinitionSatisfiesTheInterfaceItIsConsumedAs() {
        IMetricDefinition definition = new MetricDefinition("Requests", MetricUnits.Count);

        Assert.Equal("Requests", definition.Name);
        Assert.Equal("Count", definition.Units.Name);
    }

    /// <summary>
    /// The unit names are what a metrics backend is given verbatim. CloudWatch rejects a unit it
    /// does not recognise, so these strings are a contract rather than a label.
    /// </summary>
    [Theory]
    [InlineData("Milliseconds")]
    [InlineData("Seconds")]
    [InlineData("Count")]
    public void TheKnownUnitsAreNamedAsTheBackendExpects(string name) {
        var units = name switch {
            "Milliseconds" => MetricUnits.Milliseconds,
            "Seconds" => MetricUnits.Seconds,
            _ => MetricUnits.Count
        };

        Assert.Equal(name, units.Name);
    }

    [Fact]
    public void AUnitCanBeNamedForABackendTheFrameworkDoesNotKnowAbout() {
        Assert.Equal("Bytes/Second", new MetricUnits("Bytes/Second").Name);
    }

    /// <summary>
    /// The null provider hands out one shared logger. It holds no state, so allocating one per
    /// request would be pure waste on the hottest path there is.
    /// </summary>
    [Fact]
    public void TheNullProviderHandsOutOneSharedLogger() {
        var provider = new NullMetricLoggerProvider();

        Assert.Same(provider.CreateLogger("first"), provider.CreateLogger("second"));
    }

    [Fact]
    public void TheNullProviderIsSharedAcrossInstancesToo() {
        Assert.Same(
            new NullMetricLoggerProvider().CreateLogger("a"),
            new NullMetricLoggerProvider().CreateLogger("b"));
    }

    /// <summary>
    /// Every operation on the null logger is a no-op that returns rather than throws. Anything else
    /// would make "metrics not configured" break the request it was measuring.
    /// </summary>
    [Fact]
    public async Task TheNullLoggerAcceptsEveryOperationWithoutThrowing() {
        IMetricLogger logger = new NullMetricsLogger();

        logger.Record(new MetricDefinition("Requests", MetricUnits.Count), 1);
        logger.Tag("route", "/orders");
        logger.Data("orderId", 42);

        await logger.Flush();

        logger.Dispose();
    }

    [Fact]
    public void TheNullLoggersFlushIsAlreadyComplete() {
        Assert.True(new NullMetricsLogger().Flush().IsCompletedSuccessfully);
    }

    [Fact]
    public void TheNullProviderSatisfiesTheInterfaceItIsRegisteredAs() {
        Assert.IsAssignableFrom<IMetricLoggerProvider>(new NullMetricLoggerProvider());
        Assert.IsAssignableFrom<IMetricLogger>(new NullMetricsLogger());
    }
}
