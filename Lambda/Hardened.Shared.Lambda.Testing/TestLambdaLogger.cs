using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Shared.Lambda.Testing
{
    public class TestLambdaLogger : ILambdaLogger
    {
        public void Log(string message)
        {
        }

        public void LogLine(string message)
        {
        }

        public void Log(string level, string message)
        {
        }

        public void Log(LogLevel level, string message)
        {
        }

        public void LogTrace(string message)
        {
        }

        public void LogDebug(string message)
        {
        }

        public void LogInformation(string message)
        {
        }

        public void LogWarning(string message)
        {
        }

        public void LogError(string message)
        {
        }

        public void LogCritical(string message)
        {
        }
    }
}
