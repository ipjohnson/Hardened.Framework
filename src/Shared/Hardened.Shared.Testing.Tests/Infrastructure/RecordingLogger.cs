using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing.Tests.Infrastructure;

/// <summary>
/// One logged message, kept with its structured state rather than only its rendered text.
/// </summary>
/// <remarks>
/// <see cref="Hardened.Shared.Testing.Impl.TestContext"/> reports a step's outcome and duration as
/// named values in the message template, not as free text. Asserting on the rendered string alone
/// would pass for a step logged as "pass" with a duration of "fail".
/// </remarks>
internal sealed record RecordedLog(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> State) {

    /// <summary>
    /// The value logged under <paramref name="name"/>, or null if the template did not carry it.
    /// </summary>
    public object? Value(string name) {
        foreach (var pair in State) {
            if (pair.Key == name) {
                return pair.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was told, so a test can assert on it.
/// </summary>
internal sealed class RecordingLogger : ILogger {
    private readonly List<RecordedLog> _entries = new();

    public IReadOnlyList<RecordedLog> Entries {
        get {
            lock (_entries) {
                return _entries.ToArray();
            }
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ??
                     Array.Empty<KeyValuePair<string, object?>>();

        lock (_entries) {
            _entries.Add(new RecordedLog(logLevel, formatter(state, exception), exception, values));
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
