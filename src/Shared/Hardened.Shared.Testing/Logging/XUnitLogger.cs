using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Logging;

public class XUnitLogger : ILogger {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly string _loggerName;
    private readonly ITestOutputHelper _testOutputHelper;

    public XUnitLogger(
        IJsonSerializer jsonSerializer, string loggerName, ITestOutputHelper testOutputHelper) {
        _jsonSerializer = jsonSerializer;
        _loggerName = loggerName;
        _testOutputHelper = testOutputHelper;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var record = GetRecord(logLevel, eventId, state, exception, formatter);

        _testOutputHelper.WriteLine(_jsonSerializer.Serialize(record, true));
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