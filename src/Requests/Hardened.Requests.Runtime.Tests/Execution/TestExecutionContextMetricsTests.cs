using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Metrics;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The harness context's metric sink.
/// </summary>
/// <remarks>
/// <para>
/// This built its own <c>NullMetricsLogger</c> and ignored everything else, which had two effects.
/// An application had no way to write a test asserting what a request emitted — the harness
/// discarded every measurement the pipeline recorded. And <see cref="IExecutionContext.Clone"/>
/// dropped the logger it was handed, so a test written against per-fork metric attribution would
/// have passed no matter what the code under test did.
/// </para>
/// <para>
/// That second one is the reason this file exists rather than a comment: the conformance suites and
/// the Lambda batch filter both depend on a forked context carrying its own sink, and this type is
/// what those tests would be written against.
/// </para>
/// </remarks>
public class TestExecutionContextMetricsTests {

    // Typed as the interface because the implementations do not repeat Clone's optional parameters
    // - the defaults live on IExecutionContext - so a caller holding the concrete type has to pass
    // all four.
    private static IExecutionContext Create(IMetricLogger? metricLogger = null) {
        var provider = Substitute.For<IServiceProvider>();

        return new TestExecutionContext(
            provider,
            provider,
            Substitute.For<IKnownServices>(),
            Substitute.For<IExecutionRequest>(),
            Substitute.For<IExecutionResponse>(),
            CancellationToken.None,
            metricLogger);
    }

    [Fact]
    public void TheContextRecordsIntoTheLoggerItWasGiven() {
        var metricLogger = Substitute.For<IMetricLogger>();

        Assert.Same(metricLogger, Create(metricLogger).RequestMetrics);
    }

    /// <summary>
    /// Omitting one is still valid — most tests do not care — and gets the null sink this type
    /// used to hardcode.
    /// </summary>
    [Fact]
    public void OmittingALoggerFallsBackToTheNullSink() {
        Assert.IsType<NullMetricsLogger>(Create().RequestMetrics);
    }

    [Fact]
    public void CloneKeepsTheParentsLoggerWhenGivenNone() {
        var metricLogger = Substitute.For<IMetricLogger>();
        var clone = Create(metricLogger).Clone();

        Assert.Same(metricLogger, clone.RequestMetrics);
    }

    /// <summary>
    /// The one that was broken. A fork exists to be measured separately.
    /// </summary>
    [Fact]
    public void CloneTakesTheLoggerItIsGiven() {
        var parent = Substitute.For<IMetricLogger>();
        var fork = Substitute.For<IMetricLogger>();

        var clone = Create(parent).Clone(metricLogger: fork);

        Assert.Same(fork, clone.RequestMetrics);
        Assert.NotSame(parent, clone.RequestMetrics);
    }
}
