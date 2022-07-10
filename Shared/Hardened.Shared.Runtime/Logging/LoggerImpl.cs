﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Runtime.Logging
{
    public class LoggerImpl<T> : ILogger<T>
    {
        private readonly ILogger _logger;

        public LoggerImpl(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(typeof(T));
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _logger.BeginScope(state);
        }
    }
}