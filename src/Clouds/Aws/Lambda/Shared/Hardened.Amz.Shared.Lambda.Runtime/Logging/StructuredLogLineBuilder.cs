using System.Collections;
using System.Text;
using System.Text.Json;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class StructuredLogLineBuilder {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly string _logger;

    public StructuredLogLineBuilder(IJsonSerializer jsonSerializer, 
        IStringBuilderPool stringBuilderPool, string logger) {
        _jsonSerializer = jsonSerializer;
        _stringBuilderPool = stringBuilderPool;
        _logger = logger;
    }

    public string Build<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter,
        IReadOnlyList<KeyValuePair<string, object?>>? scopeProperties = null) {
        using var stringBuilder = _stringBuilderPool.Get();

        stringBuilder.Item.Append("{");
        
        AppendKeyedStringValue(stringBuilder.Item, "logger", _logger);
        AppendKeyedStringValue(stringBuilder.Item, "logLevel", logLevel.ToString());
        AppendKeyedStringValue(stringBuilder.Item, "eventId", eventId.ToString());
        
        if (exception != null) {
            AppendKeyedStringValue(stringBuilder.Item, "exception", exception.Message);
            AppendKeyedStringValue(stringBuilder.Item, "exceptionType", exception.GetType().Name);
            AppendKeyedStringValue(stringBuilder.Item, "stackTrace", exception.StackTrace ?? "");
        }
        
        if (state is IEnumerable<KeyValuePair<string,object?>> tupleData) {
            foreach (var keyValuePair in tupleData) {
                if (keyValuePair.Key == "{OriginalFormat}" || 
                    keyValuePair.Value == null) {
                    continue;
                }

                if (keyValuePair.Value is string || 
                    (keyValuePair.Value?.GetType().IsPrimitive ?? false)) {
                    AppendKeyedStringValue(stringBuilder.Item,"request." + keyValuePair.Key, keyValuePair.Value?.ToString() ?? "");
                }
                else {
                    var valueString = _jsonSerializer.Serialize(keyValuePair.Value!);
                    
                    stringBuilder.Item.Append($"\"request.{keyValuePair.Key}\":\"{valueString}\",");
                }
            }
        }
        else {
            var serializedState = _jsonSerializer.Serialize(state!);
            
            stringBuilder.Item.Append("\"request\":");
            stringBuilder.Item.Append(serializedState);
            stringBuilder.Item.Append(',');
            
            stringBuilder.Item.Append($"\"stateType\":\"{state?.GetType().Name}\",");
        }
        
        if (scopeProperties is { Count: > 0 }) {
            foreach (var kvp in scopeProperties) {
                if (kvp.Value is null) continue;
                AppendKeyedStringValue(stringBuilder.Item, kvp.Key, kvp.Value.ToString() ?? "");
            }
        }

        AppendKeyedStringValue(stringBuilder.Item, "message", formatter(state, exception), false);
        
        stringBuilder.Item.Append("}");

        return stringBuilder.Item.ToString();
    }

    private void AppendKeyedStringValue(StringBuilder stringBuilder, string key, string value, bool includeComma = true) {
        stringBuilder.Append('"');
        stringBuilder.Append(key);
        stringBuilder.Append("\":\"");
        WriteEscapedStringToBuilder(stringBuilder, value);
        stringBuilder.Append('"');

        if (includeComma)
            stringBuilder.Append(',');
    }
    
    private void WriteEscapedStringToBuilder(StringBuilder stringBuilder, string value) {

        foreach (char c in value) {
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