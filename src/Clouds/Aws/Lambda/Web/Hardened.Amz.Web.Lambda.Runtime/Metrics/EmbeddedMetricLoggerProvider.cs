using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Web.Lambda.Runtime.Metrics;

[SingletonService]
public class EmbeddedMetricLoggerProvider : IMetricLoggerProvider {
    private readonly IDimensionSetProvider _dimensionSetProvider;

    public EmbeddedMetricLoggerProvider(IDimensionSetProvider dimensionSetProvider) {
        _dimensionSetProvider = dimensionSetProvider;
    }

    public IMetricLogger CreateLogger(string loggerName) {
        return new EmbeddedMetricLogger(loggerName, _dimensionSetProvider);
    }
}
