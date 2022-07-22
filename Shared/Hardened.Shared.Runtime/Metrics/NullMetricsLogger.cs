using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Metrics
{
    public class NullMetricsLogger : IMetricLogger
    {
        public void Dispose()
        {
            
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
}
