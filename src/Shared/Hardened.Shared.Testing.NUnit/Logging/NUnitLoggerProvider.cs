using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NUnitTestContext = NUnit.Framework.TestContext;

namespace Hardened.Shared.Testing.Logging;

/// <summary>
/// Writes a test's log lines to NUnit's <see cref="NUnitTestContext.Out"/>, which the runner
/// captures into the running test's output.
/// </summary>
public class NUnitLoggerProvider : ILoggerProvider {
    private readonly ConcurrentDictionary<string, NUnitLogger> _loggers = new();

    public void Dispose() { }

    public ILogger CreateLogger(string categoryName) {
        return _loggers.GetOrAdd(categoryName, name => new NUnitLogger(name));
    }
}

/// <summary>
/// One structured JSON record per line, the shape the xUnit logger writes, so a log line reads
/// the same under either runner.
/// </summary>
public class NUnitLogger : ILogger {
    private static readonly JsonSerializerOptions LogSerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _loggerName;

    public NUnitLogger(string loggerName) {
        _loggerName = loggerName;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        ExceptionRecord? exceptionRecord = null;

        if (exception != null) {
            exceptionRecord = new ExceptionRecord(
                exception.GetType().Name, exception.Message, exception.StackTrace ?? "empty");
        }

        var record = new StructuredLogEntry<TState>(
            DateTime.Now, _loggerName, logLevel, eventId, formatter(state, exception), state, exceptionRecord);

        NUnitTestContext.Out.WriteLine(JsonSerializer.Serialize(record, LogSerializerOptions));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;

    public record StructuredLogEntry<TState>(
        DateTime Timestamp,
        string Logger,
        LogLevel LogLevel,
        EventId EventId,
        string Message,
        TState Data,
        ExceptionRecord? Exception);

    public record ExceptionRecord(string Type, string Message, string StackTrace);
}
