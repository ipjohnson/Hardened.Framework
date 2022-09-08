using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Metrics
{
    public interface IMetricDefinition
    {
        string Name { get; }

        MetricUnits Units { get; }
    }

    public class MetricDefinition : IMetricDefinition
    {
        public MetricDefinition(string name, MetricUnits units)
        {
            Name = name;
            Units = units;
        }

        public string Name { get; }

        public MetricUnits Units { get; }
    }
}
