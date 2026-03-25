using System.Collections;
using System.Text;
using System.Text.Json;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class StructuredLogLineBuilder {
    private const int DefaultMaxLogLineLength = 3 * 1024;
    private const int DefaultMaxFieldLength = 1024;
    private const int DefaultMaxStackTraceLength = 512;
    private const string TruncationMarker = "...[truncated]";

    private readonly IJsonSerializer _jsonSerializer;
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly string _logger;
    private readonly int _maxLogLineLength;
    private readonly int _maxFieldLength;
    private readonly int _maxStackTraceLength;

    public StructuredLogLineBuilder(IJsonSerializer jsonSerializer,
        IStringBuilderPool stringBuilderPool, string logger,
        int maxLogLineLength = DefaultMaxLogLineLength,
        int maxFieldLength = DefaultMaxFieldLength,
        int maxStackTraceLength = DefaultMaxStackTraceLength) {
        _jsonSerializer = jsonSerializer;
        _stringBuilderPool = stringBuilderPool;
        _logger = logger;
        _maxLogLineLength = maxLogLineLength;
        _maxFieldLength = maxFieldLength;
        _maxStackTraceLength = maxStackTraceLength;
    }

    public string Build<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter,
        IReadOnlyList<KeyValuePair<string, object?>>? scopeProperties = null,
        long beginScopeCallCount = 0,
        bool scopeActive = false) {
        using var stringBuilder = _stringBuilderPool.Get();

        stringBuilder.Item.Append("{");
        
        AppendKeyedStringValue(stringBuilder.Item, "logger", _logger);
        AppendKeyedStringValue(stringBuilder.Item, "logLevel", logLevel.ToString());
        AppendKeyedStringValue(stringBuilder.Item, "eventId", eventId.ToString());
        
        if (exception != null) {
            AppendKeyedStringValue(stringBuilder.Item, "exception", exception.Message, maxValueLength: _maxFieldLength);
            AppendKeyedStringValue(stringBuilder.Item, "exceptionType", exception.GetType().Name);
            AppendKeyedStringValue(stringBuilder.Item, "stackTrace", exception.StackTrace ?? "", maxValueLength: _maxStackTraceLength);
        }
        
        if (state is IEnumerable<KeyValuePair<string,object?>> tupleData) {
            foreach (var keyValuePair in tupleData) {
                if (keyValuePair.Key == "{OriginalFormat}" || 
                    keyValuePair.Value == null) {
                    continue;
                }

                if (keyValuePair.Value is string ||
                    (keyValuePair.Value?.GetType().IsPrimitive ?? false)) {
                    AppendKeyedStringValue(stringBuilder.Item,"request." + keyValuePair.Key, keyValuePair.Value?.ToString() ?? "", maxValueLength: _maxFieldLength);
                }
                else {
                    var valueString = _jsonSerializer.Serialize(keyValuePair.Value!);
                    valueString = Truncate(valueString, _maxFieldLength);

                    stringBuilder.Item.Append($"\"request.{keyValuePair.Key}\":\"{valueString}\",");
                }
            }
        }
        else {
            var serializedState = _jsonSerializer.Serialize(state!);
            serializedState = Truncate(serializedState, _maxFieldLength);

            stringBuilder.Item.Append("\"request\":");
            stringBuilder.Item.Append(serializedState);
            stringBuilder.Item.Append(',');

            stringBuilder.Item.Append($"\"stateType\":\"{state?.GetType().Name}\",");
        }
        
        // Diagnostic: emit scope debug info to verify scope propagation in CloudWatch
        stringBuilder.Item.Append("\"__scopeCount\":");
        stringBuilder.Item.Append(scopeProperties?.Count ?? 0);
        stringBuilder.Item.Append(",\"__beginScopeCalls\":");
        stringBuilder.Item.Append(beginScopeCallCount);
        stringBuilder.Item.Append(",\"__scopeActive\":");
        stringBuilder.Item.Append(scopeActive ? "true" : "false");
        stringBuilder.Item.Append(',');

        if (scopeProperties is { Count: > 0 }) {
            foreach (var kvp in scopeProperties) {
                if (kvp.Value is null) continue;
                AppendKeyedStringValue(stringBuilder.Item, kvp.Key, kvp.Value.ToString() ?? "");
            }
        }

        AppendKeyedStringValue(stringBuilder.Item, "message", formatter(state, exception), includeComma: false, maxValueLength: _maxFieldLength);

        stringBuilder.Item.Append("}");

        if (stringBuilder.Item.Length > _maxLogLineLength) {
            stringBuilder.Item.Length = _maxLogLineLength - TruncationMarker.Length - 1;
            stringBuilder.Item.Append(TruncationMarker);
            stringBuilder.Item.Append('}');
        }

        return stringBuilder.Item.ToString();
    }

    private void AppendKeyedStringValue(StringBuilder stringBuilder, string key, string value, bool includeComma = true, int maxValueLength = 0) {
        stringBuilder.Append('"');
        stringBuilder.Append(key);
        stringBuilder.Append("\":\"");
        WriteEscapedStringToBuilder(stringBuilder, value, maxValueLength > 0 ? maxValueLength : _maxFieldLength);
        stringBuilder.Append('"');

        if (includeComma)
            stringBuilder.Append(',');
    }

    private static string Truncate(string value, int maxLength) {
        if (value.Length <= maxLength) return value;
        return string.Concat(value.AsSpan(0, maxLength - TruncationMarker.Length), TruncationMarker);
    }

    private void WriteEscapedStringToBuilder(StringBuilder stringBuilder, string value, int maxLength = 0) {
        var effectiveMax = maxLength > 0 ? maxLength : _maxFieldLength;
        var startLength = stringBuilder.Length;

        foreach (char c in value) {
            if (stringBuilder.Length - startLength >= effectiveMax) {
                stringBuilder.Append(TruncationMarker);
                return;
            }

            switch (c) {
                case '"':    
                    stringBuilder.Append("\\\"");
                    break;
                case '\\':
                    stringBuilder.Append("\\\\");
                    break;
                case '\b':
                    stringBuilder.Append("\\b");
                    break;
                case '\f':
                    stringBuilder.Append("\\f");
                    break;
                case '\n':
                    stringBuilder.Append("\\n");
                    break;
                case '\r':
                    stringBuilder.Append("\\r");
                    break;
                case '\t':
                    stringBuilder.Append("\\t");
                    break;

                default:
                    if (char.IsControl(c)) {
                        stringBuilder.Append("\\u");
                        stringBuilder.Append(((int)c).ToString("x4"));
                    }
                    else {
                        stringBuilder.Append(c);
                    }
                    break;
            }
        }
    }
}