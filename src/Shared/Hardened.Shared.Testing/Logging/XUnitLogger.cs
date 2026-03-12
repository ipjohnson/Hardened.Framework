using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using XunitTestContext = Xunit.TestContext;

namespace Hardened.Shared.Testing.Logging;

public class XUnitLogger : ILogger {
    private static readonly JsonSerializerOptions LogSerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _loggerName;

    public XUnitLogger(string loggerName) {
        _loggerName = loggerName;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var record = GetRecord(logLevel, eventId, state, exception, formatter);

        var output = XunitTestContext.Current?.TestOutputHelper;
        output?.WriteLine(JsonSerializer.Serialize(record, LogSerializerOptions));
    }

    private StructuredLogEntry<TState> GetRecord<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception, string> formatter) {
        ExceptionRecord? exceptionRecord = null;

        if (exception != null) {
            exceptionRecord = new ExceptionRecord(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? "empty"
            );
        }

        return new StructuredLogEntry<TState>(
            DateTime.Now,
            _loggerName,
            logLevel,
            eventId,
            formatter(state, exception!),
            state,
            exceptionRecord
        );
    }

    public bool IsEnabled(LogLevel logLevel) {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;


    public record StructuredLogEntry<TState>(
        DateTime Timestamp,
        string Logger,
        LogLevel LogLevel,
        EventId EventId,
        string Message,
        TState Data,
        ExceptionRecord? Exception
    );

    public record ExceptionRecord(
        string Type,
        string Message,
        string StackTrace);
}
