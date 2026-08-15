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

    /// <summary>
    /// What a truncated line ends with. A field rather than a bare marker, so the line stays valid
    /// JSON and so a query can find the lines that lost content.
    /// </summary>
    private const string TruncatedField = "\"truncated\":true,";

    private const int TruncatedFieldLength = 17;

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

    /// <summary>
    /// Builds one CloudWatch log line.
    ///
    /// <para>
    /// The output is always parseable JSON, including when it is truncated. That is the whole point
    /// of a structured logger — a line Logs Insights cannot parse is worse than a line that was
    /// never written, because it looks present and is unqueryable — and it was not true of this
    /// builder until 2026-08-15. Two cases produced invalid JSON: a non-primitive value in the
    /// state bag, whose serialized JSON was interpolated into a quoted string without escaping, and
    /// whole-line truncation, which cut at a byte offset and closed the object over a half-written
    /// string. Both were reachable by ordinary logging at the default limits.
    /// </para>
    /// </summary>
    public string Build<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter,
        IReadOnlyList<KeyValuePair<string, object?>>? scopeProperties = null) {
        using var stringBuilder = _stringBuilderPool.Get();
        var builder = stringBuilder.Item;

        // The offset of the last complete "key":value, — the only place the line can be cut and
        // still close into valid JSON. Everything appended past the budget is dropped whole rather
        // than sliced.
        var lastFieldEnd = 1;
        var budget = _maxLogLineLength - TruncatedFieldLength;

        builder.Append('{');

        // message goes first so that truncation drops context, never the thing being said. JSON
        // objects are unordered, so nothing downstream depends on where it sits.
        AppendKeyedStringValue(builder, "message", SafeFormat(state, exception, formatter),
            maxValueLength: _maxFieldLength);
        MarkFieldEnd();

        AppendKeyedStringValue(builder, "logger", _logger);
        AppendKeyedStringValue(builder, "logLevel", logLevel.ToString());
        AppendKeyedStringValue(builder, "eventId", eventId.ToString());
        MarkFieldEnd();

        if (exception != null) {
            AppendKeyedStringValue(builder, "exception", exception.Message, maxValueLength: _maxFieldLength);
            AppendKeyedStringValue(builder, "exceptionType", exception.GetType().Name);
            MarkFieldEnd();
            AppendKeyedStringValue(builder, "stackTrace", exception.StackTrace ?? "", maxValueLength: _maxStackTraceLength);
            MarkFieldEnd();
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> tupleData) {
            foreach (var keyValuePair in tupleData) {
                if (keyValuePair.Key == "{OriginalFormat}" ||
                    keyValuePair.Value == null) {
                    continue;
                }

                AppendKeyedValue(builder, "request." + keyValuePair.Key, keyValuePair.Value);
                MarkFieldEnd();
            }
        }
        else if (state != null) {
            AppendKeyedValue(builder, "request", state);
            AppendKeyedStringValue(builder, "stateType", state.GetType().Name);
            MarkFieldEnd();
        }

        if (scopeProperties is { Count: > 0 }) {
            foreach (var kvp in scopeProperties) {
                if (kvp.Value is null) continue;
                AppendKeyedStringValue(builder, kvp.Key, kvp.Value.ToString() ?? "");
                MarkFieldEnd();
            }
        }

        if (builder.Length > budget) {
            builder.Length = lastFieldEnd;
            builder.Append(TruncatedField);
        }

        // Every field above is written with a trailing comma, so the line always ends on one and
        // there is exactly one place that has to remove it.
        if (builder[builder.Length - 1] == ',') {
            builder.Length -= 1;
        }

        builder.Append('}');

        return builder.ToString();

        void MarkFieldEnd() {
            if (builder.Length <= budget) {
                lastFieldEnd = builder.Length;
            }
        }
    }

    /// <summary>
    /// A formatter is the caller's code and runs while something has already gone wrong often
    /// enough to be worth not trusting. Losing the structured line because the message could not be
    /// rendered would take the log entry down with it.
    /// </summary>
    private static string SafeFormat<TState>(
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        try {
            return formatter(state, exception);
        }
        catch (Exception exp) {
            return "[formatter threw " + exp.GetType().Name + "]";
        }
    }

    /// <summary>
    /// Writes a state value at whatever fidelity is available, and never throws.
    ///
    /// <para>
    /// Strings and primitives go out as strings. Everything else is serialized, and emitted inline
    /// when the result fits so it stays queryable as structure. When it does not fit it is written
    /// as an escaped string instead — truncated JSON is not JSON, so it cannot be emitted inline.
    /// </para>
    ///
    /// <para>
    /// The serializer is the AOT hazard. <c>IsPrimitive</c> is false for <c>TimeSpan</c>,
    /// <c>DateTime</c>, <c>Guid</c>, <c>decimal</c> and every enum, so all of them reach it, and
    /// under NativeAOT with source-generated serialization it throws for any type the application's
    /// context does not cover. That exception used to leave the logger and replace the line being
    /// written — most often a line about the failure that produced the value. Falling back to
    /// <c>ToString()</c> keeps the entry; a logger must not be able to throw.
    /// </para>
    /// </summary>
    private void AppendKeyedValue(StringBuilder stringBuilder, string key, object value) {
        if (value is string || value.GetType().IsPrimitive) {
            AppendKeyedStringValue(stringBuilder, key, value.ToString() ?? "", maxValueLength: _maxFieldLength);
            return;
        }

        string? serialized = null;

        try {
            serialized = _jsonSerializer.Serialize(value);
        }
        catch (Exception) {
            // Deliberately broad. Serializers throw NotSupportedException for a missing
            // JsonTypeInfo, JsonException for a cycle, and whatever a custom converter decides on.
            // None of them are worth a lost log line.
        }

        if (serialized == null) {
            AppendKeyedStringValue(stringBuilder, key, value.ToString() ?? "", maxValueLength: _maxFieldLength);
            return;
        }

        if (serialized.Length <= _maxFieldLength) {
            stringBuilder.Append('"');
            stringBuilder.Append(key);
            stringBuilder.Append("\":");
            stringBuilder.Append(serialized);
            stringBuilder.Append(',');
            return;
        }

        AppendKeyedStringValue(stringBuilder, key, serialized, maxValueLength: _maxFieldLength);
    }

    /// <summary>
    /// Always writes a trailing comma. <see cref="Build{TState}"/> strips the last one once, which
    /// is the only way the field order can be rearranged — as it was, to put the message first —
    /// without a "is this the last field" flag threaded through every call site.
    /// </summary>
    private void AppendKeyedStringValue(StringBuilder stringBuilder, string key, string value, int maxValueLength = 0) {
        stringBuilder.Append('"');
        stringBuilder.Append(key);
        stringBuilder.Append("\":\"");
        WriteEscapedStringToBuilder(stringBuilder, value, maxValueLength > 0 ? maxValueLength : _maxFieldLength);
        stringBuilder.Append('"');
        stringBuilder.Append(',');
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