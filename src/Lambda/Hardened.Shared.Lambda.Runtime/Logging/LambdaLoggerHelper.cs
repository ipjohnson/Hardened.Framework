using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Lambda.Runtime.Logging;

public class LambdaLoggerHelper
{
    private readonly LogLevel _logLevel;
    private readonly string _entryNamespace;

    public LambdaLoggerHelper(LogLevel logLevel, string entryNamespace)
    {
        _logLevel = logLevel;
        _entryNamespace = entryNamespace;
    }

    public void BuildLogger(ILoggingBuilder builder)
    {
        builder
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddFilter("System", LogLevel.Warning)
            .AddFilter("Hardened", _logLevel)
            .AddFilter(_entryNamespace, _logLevel)
            .AddLambdaLogger();
    }

    public static Action<ILoggingBuilder> CreateAction(IEnvironment environment, string entryNamespace)
    {
        LogLevel logLevel = LogLevel.Information;

        if (environment.Name == "development" || environment.Name == "test")
        {
            logLevel = LogLevel.Debug;
        }

        var logLevelString = environment.Value<string>("LOG_LEVEL");

        if (logLevelString != null)
        {
            if (Enum.TryParse(typeof(LogLevel), logLevelString, out var newLogLevel))
            {
                logLevel = (LogLevel)newLogLevel!;
            }
        }

        return new LambdaLoggerHelper(logLevel, entryNamespace).BuildLogger;
    }

    public static Action<ILoggingBuilder> CreateAction(LogLevel logLevel, string entryNamespace)
    {
        return new LambdaLoggerHelper(logLevel, entryNamespace).BuildLogger;
    }
}