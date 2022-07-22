using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Metrics
{
    public interface IMetricLoggerProvider
    {
        IMetricLogger CreateLogger(string loggerName);
    }
}
