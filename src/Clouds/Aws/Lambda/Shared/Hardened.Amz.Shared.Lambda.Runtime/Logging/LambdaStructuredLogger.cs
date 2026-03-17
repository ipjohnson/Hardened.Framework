using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaStructuredLogger : ILogger {
    private static readonly AsyncLocal<LogScope?> _currentScope = new();
    private static long _beginScopeCallCount;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly StructuredLogLineBuilder _structuredLogLineBuilder;

    public LambdaStructuredLogger(
        IJsonSerializer jsonSerializer,
        ILambdaContextAccessor lambdaContextAccessor,
        IStringBuilderPool stringBuilderPool,
        string categoryName) {
        _jsonSerializer = jsonSerializer;
        _lambdaContextAccessor = lambdaContextAccessor;
        _structuredLogLineBuilder = new StructuredLogLineBuilder(jsonSerializer, stringBuilderPool, categoryName);

    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var scopeProperties = GetScopeProperties();
        var serializedData = _structuredLogLineBuilder.Build(logLevel, eventId, state, exception, formatter, scopeProperties,
            BeginScopeCallCount, _currentScope.Value != null);

        _lambdaContextAccessor.Context!.Logger.LogLine(serializedData);
    }

    public bool IsEnabled(LogLevel logLevel) {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
        Interlocked.Increment(ref _beginScopeCallCount);
        var scope = new LogScope(state, _currentScope.Value);
        _currentScope.Value = scope;
        return scope;
    }

    internal static long BeginScopeCallCount => Interlocked.Read(ref _beginScopeCallCount);

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
