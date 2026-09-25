using Microsoft.Extensions.Logging;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>Keeps what was logged, for behaviour whose only output is a warning.</summary>
internal sealed class CollectingLogger<T>(List<string> messages) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => messages.Add(formatter(state, exception));
}
