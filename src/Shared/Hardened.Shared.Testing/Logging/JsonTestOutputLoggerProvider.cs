using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing.Logging;

/// <summary>
/// Writes each log entry to the running test's output, as an indented JSON record.
/// </summary>
/// <remarks>
/// One logger for every test framework. It writes through <see cref="CurrentTest"/>, whose
/// provider the test package installs - DependencyModules.xUnit, DependencyModules.xUnit4 or
/// DependencyModules.NUnit - and which knows where its framework shows a test's output. An entry
/// logged while no test is running is dropped, which includes a background task that outlives its
/// test.
/// </remarks>
internal sealed class JsonTestOutputLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, JsonTestOutputLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new JsonTestOutputLogger(name));

    public void Dispose() { }
}

internal sealed class JsonTestOutputLogger(string loggerName) : ILogger
{
    private static readonly JsonSerializerOptions LogSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        ExceptionRecord? exceptionRecord = null;

        if (exception != null)
        {
            exceptionRecord = new ExceptionRecord(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? "empty"
            );
        }

        var record = new StructuredLogEntry<TState>(
            DateTime.Now,
            loggerName,
            logLevel,
            eventId,
            formatter(state, exception),
            state,
            exceptionRecord
        );

        CurrentTest.TryWriteLine(JsonSerializer.Serialize(record, LogSerializerOptions));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public record StructuredLogEntry<TState>(
        DateTime Timestamp,
        string Logger,
        LogLevel LogLevel,
        EventId EventId,
        string Message,
        TState Data,
        ExceptionRecord? Exception
    );

    public record ExceptionRecord(string Type, string Message, string StackTrace);
}
