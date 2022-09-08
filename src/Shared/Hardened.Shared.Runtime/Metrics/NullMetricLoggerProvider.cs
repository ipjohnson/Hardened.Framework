using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Metrics
{
    public class NullMetricLoggerProvider : IMetricLoggerProvider
    {
        private static readonly IMetricLogger _logger = new NullMetricsLogger();

        public IMetricLogger CreateLogger(string loggerName)
        {
            return _logger;
        }
    }
}
