using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Shared.Runtime.Metrics
{
    public interface IMetricLogger : IDisposable
    {
        void Record(IMetricDefinition metric, double value);

        void Tag(string tagName, object tagValue);

        void Data(string dataName, object dataValue);
    }
}
