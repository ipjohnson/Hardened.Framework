using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaStructuredLogger : ILogger {
    private static readonly AsyncLocal<LogScope?> _currentScope = new();
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly StructuredLogLineBuilder _structuredLogLineBuilder;

    public LambdaStructuredLogger(
        IJsonSerializer jsonSerializer,
        ILambdaContextAccessor lambdaContextAccessor,
        IStringBuilderPool stringBuilderPool,
        string categoryName) {
        _lambdaContextAccessor = lambdaContextAccessor;
        _structuredLogLineBuilder = new StructuredLogLineBuilder(jsonSerializer, stringBuilderPool, categoryName);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var serializedData = _structuredLogLineBuilder.Build(
            logLevel, eventId, state, exception, formatter, GetScopeProperties());

        _lambdaContextAccessor.Context!.Logger.LogLine(serializedData);
    }

    public bool IsEnabled(LogLevel logLevel) {
        return true;
    }

    // A process-wide Interlocked.Increment used to run here, feeding a __beginScopeCalls field on
    // every log line. Both were left over from an investigation into scope propagation: a
    // never-reset global counter and three __-prefixed fields on every line, billed per invocation.
    // Removed 2026-08-15.
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
        var scope = new LogScope(state, _currentScope.Value);
        _currentScope.Value = scope;
        return scope;
    }

    private static IReadOnlyList<KeyValuePair<string, object?>>? GetScopeProperties() {
        var scope = _currentScope.Value;
        if (scope is null) return null;

        var properties = new List<KeyValuePair<string, object?>>();
        var current = scope;
        while (current is not null) {
            properties.AddRange(current.Properties);
            current = current.Parent;
        }
        return properties;
    }

    private sealed class LogScope(object state, LogScope? parent) : IDisposable {
        public LogScope? Parent { get; } = parent;
        public KeyValuePair<string, object?>[] Properties { get; } = state switch {
            IEnumerable<KeyValuePair<string, object?>> kvps => kvps.ToArray(),
            _ => [new KeyValuePair<string, object?>("scope", state.ToString())]
        };

        public void Dispose() => _currentScope.Value = Parent;
    }
}
