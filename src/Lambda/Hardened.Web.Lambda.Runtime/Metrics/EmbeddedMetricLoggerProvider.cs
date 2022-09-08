using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Lambda.Runtime.Metrics
{
    public class EmbeddedMetricLoggerProvider : IMetricLoggerProvider
    {
        private readonly IDimensionSetProvider _dimensionSetProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<EmbeddedMetricLoggerProvider> _logger;

        public EmbeddedMetricLoggerProvider(IDimensionSetProvider dimensionSetProvider, ILoggerFactory loggerFactory, ILogger<EmbeddedMetricLoggerProvider> logger)
        {
            _dimensionSetProvider = dimensionSetProvider;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        public IMetricLogger CreateLogger(string loggerName)
        {
            _logger.LogInformation("Create logger " + loggerName);
            
            return new EmbeddedMetricLogger(loggerName, _dimensionSetProvider, _loggerFactory);
        }
    }
}
