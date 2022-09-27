using Amazon.CloudWatch.EMF.Config;
using Amazon.CloudWatch.EMF.Environment;
using Amazon.CloudWatch.EMF.Logger;
using Amazon.CloudWatch.EMF.Model;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Lambda.Runtime.Metrics
{
    public class EmbeddedMetricLogger : IMetricLogger
    {
        private int _disposed;
        private readonly MetricsLogger _metricsLogger;
        private readonly IDimensionSetProvider _dimensionSetProvider;
        private readonly List<Tuple<string, object>> _tags = new();

        public EmbeddedMetricLogger(string loggerName, IDimensionSetProvider dimensionSetProvider, ILoggerFactory loggerFactory)
        {
            _dimensionSetProvider = dimensionSetProvider;
            _metricsLogger = new MetricsLogger(new LambdaEnvironment(new Configuration(), loggerFactory), loggerFactory);
            _metricsLogger.SetNamespace(loggerName);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                foreach (var dimensionSet in _dimensionSetProvider.Get(_tags))
                {
                    _metricsLogger.PutDimensions(dimensionSet);
                }

                _metricsLogger.Dispose();
            }
        }
        
        public void Record(IMetricDefinition metric, double value)
        {
            var unit = Unit.COUNT;

            switch (metric.Units.Name)
            {
                case "Milliseconds":
                    unit = Unit.MILLISECONDS;
                    break;
            }

            _metricsLogger.PutMetric(metric.Name, value, unit);
        }

        public void Tag(string tagName, object tagValue)
        {
            _tags.Add(new Tuple<string, object>(tagName, tagValue));
        }

        public void Data(string dataName, object dataValue)
        {
            _metricsLogger.PutProperty(dataName, dataValue);
        }
    }
}
