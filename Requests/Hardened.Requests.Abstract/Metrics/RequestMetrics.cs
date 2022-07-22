using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Abstract.Metrics
{
    public static class RequestMetrics
    {
        public static readonly IMetricDefinition TotalRequestDuration =
            new MetricDefinition("TotalRequestDuration", MetricUnits.Milliseconds);

        public static readonly IMetricDefinition ResponseDuration =
            new MetricDefinition("ResponseDuration", MetricUnits.Milliseconds);

        public static readonly IMetricDefinition ParameterBindDuration =
            new MetricDefinition("ParameterBindDuration", MetricUnits.Milliseconds);
        
        public static readonly IMetricDefinition HandlerInvokeDuration =
            new MetricDefinition("HandlerDuration", MetricUnits.Milliseconds);
    }
}
