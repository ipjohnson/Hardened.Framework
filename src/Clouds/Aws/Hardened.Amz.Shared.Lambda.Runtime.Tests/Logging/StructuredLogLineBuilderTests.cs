using System.Text;
using System.Text.Json;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Logging;

public class StructuredLogLineBuilderTests {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly StructuredLogLineBuilder _builder;

    public StructuredLogLineBuilderTests() {
        _jsonSerializer = Substitute.For<IJsonSerializer>();
        var stringBuilderPool = new StringBuilderPool();
        _builder = new StructuredLogLineBuilder(_jsonSerializer, stringBuilderPool, "TestLogger");
    }

    [Fact]
    public void ProducesValidJsonOutput() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test message")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1, "Test"),
            state, null, (s, e) => "test message");

        var doc = JsonDocument.Parse(result);
        Assert.NotNull(doc);
    }

    [Fact]
    public void IncludesLoggerName_LogLevel_EventId_Message() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test message")
        };

        var result = _builder.Build(LogLevel.Warning, new EventId(5, "TestEvent"),
            state, null, (s, e) => "my test message");

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("TestLogger", root.GetProperty("logger").GetString());
        Assert.Equal("Warning", root.GetProperty("logLevel").GetString());
        Assert.Contains("TestEvent", root.GetProperty("eventId").GetString());
        Assert.Equal("my test message", root.GetProperty("message").GetString());
    }

    [Fact]
    public void IncludesExceptionFields_WhenExceptionProvided() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "error occurred")
        };
        var exception = new InvalidOperationException("something went wrong");

        var result = _builder.Build(LogLevel.Error, new EventId(1),
            state, exception, (s, e) => "error occurred");

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("something went wrong", root.GetProperty("exception").GetString());
        Assert.Equal("InvalidOperationException", root.GetProperty("exceptionType").GetString());
        Assert.True(root.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public void OmitsExceptionFields_WhenExceptionIsNull() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "no error")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "no error");

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("exception", out _));
        Assert.False(root.TryGetProperty("exceptionType", out _));
        Assert.False(root.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public void HandlesKeyedState_StringAndPrimitiveValues() {
        var state = new List<KeyValuePair<string, object?>> {
            new("userId", "user-123"),
            new("count", 42),
            new("{OriginalFormat}", "test")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test");

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("user-123", root.GetProperty("request.userId").GetString());
        Assert.Equal("42", root.GetProperty("request.count").GetString());
    }

    /// <summary>
    /// The branch that produced invalid JSON until 2026-08-15. A non-primitive value was serialized
    /// and then interpolated into a quoted string without escaping, so the value's own quotes closed
    /// the string early and CloudWatch could not parse the line.
    /// </summary>
    [Fact]
    public void HandlesKeyedState_ComplexValueStaysParseable() {
        _jsonSerializer.Serialize(Arg.Any<object>()).Returns("{\"Name\":\"widget\",\"Qty\":3}");

        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "order {order}"),
            new("order", new { Name = "widget", Qty = 3 })
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1), state, null, (s, e) => "order");

        var order = JsonDocument.Parse(result).RootElement.GetProperty("request.order");

        // Inline, not stringified: the point of serializing it is that it stays queryable.
        Assert.Equal(JsonValueKind.Object, order.ValueKind);
        Assert.Equal("widget", order.GetProperty("Name").GetString());
        Assert.Equal(3, order.GetProperty("Qty").GetInt32());
    }

    /// <summary>
    /// Serialized JSON too large for a field cannot be emitted inline — a truncated object is not
    /// an object — so it degrades to an escaped string rather than to a broken line.
    /// </summary>
    [Fact]
    public void HandlesKeyedState_OversizedComplexValueDegradesToAString() {
        _jsonSerializer.Serialize(Arg.Any<object>())
            .Returns("{\"blob\":\"" + new string('z', 4000) + "\"}");

        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "big"),
            new("payload", new object())
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1), state, null, (s, e) => "big");

        var payload = JsonDocument.Parse(result).RootElement.GetProperty("request.payload");

        Assert.Equal(JsonValueKind.String, payload.ValueKind);
    }

    /// <summary>
    /// The AOT failure from AMZ-LAMBDA-FINDINGS.md §3, and every sibling it had.
    /// <c>Type.IsPrimitive</c> is false for <c>TimeSpan</c>, <c>DateTime</c>, <c>Guid</c>,
    /// <c>decimal</c> and every enum, so all of them reach the serializer — which, under NativeAOT
    /// with source-generated serialization, throws for any type the application's context does not
    /// cover. The exception used to leave the logger and take the log line with it.
    /// </summary>
    [Fact]
    public void HandlesKeyedState_SerializerFailureFallsBackInsteadOfThrowing() {
        _jsonSerializer.Serialize(Arg.Any<object>())
            .Returns(_ => throw new NotSupportedException(
                "JsonTypeInfo metadata for type 'System.TimeSpan' was not provided"));

        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "took {elapsed}"),
            new("elapsed", TimeSpan.FromSeconds(3))
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1), state, null, (s, e) => "took");

        var root = JsonDocument.Parse(result).RootElement;

        Assert.Equal(TimeSpan.FromSeconds(3).ToString(), root.GetProperty("request.elapsed").GetString());
        Assert.Equal("took", root.GetProperty("message").GetString());
    }

    /// <summary>
    /// A formatter is the caller's code, and it runs most often when something has already gone
    /// wrong. It must not be able to take the entry down with it.
    /// </summary>
    [Fact]
    public void AFormatterThatThrowsStillProducesALine() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "boom")
        };

        var result = _builder.Build(LogLevel.Error, new EventId(1), state, null,
            (s, e) => throw new InvalidOperationException("no"));

        var root = JsonDocument.Parse(result).RootElement;

        Assert.Contains("InvalidOperationException", root.GetProperty("message").GetString());
    }

    [Fact]
    public void EscapesSpecialCharacters() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "line1\nline2\ttab\"quote\\backslash\r\b\f");

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        var message = root.GetProperty("message").GetString();
        Assert.Contains("line1\nline2", message);
        Assert.Contains("\"", message);
        Assert.Contains("\\", message);
    }

    [Fact]
    public void EscapesControlCharacters_AsUnicodeSequences() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test")
        };

        // \x01 is a control character that should be escaped as \u0001
        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "hello\x01world");

        Assert.Contains("\\u0001", result);
    }

    /// <summary>
    /// A string state does not reach the serializer at all — it is already a JSON scalar, and every
    /// call avoided is one less type an application's AOT serializer context has to cover.
    /// </summary>
    [Fact]
    public void HandlesNonKeyedState_StringGoesOutDirectly() {
        var state = "just a plain string";

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test message");

        var root = JsonDocument.Parse(result).RootElement;

        Assert.Equal("just a plain string", root.GetProperty("request").GetString());
        Assert.Equal("String", root.GetProperty("stateType").GetString());
        _jsonSerializer.DidNotReceive().Serialize(Arg.Any<object>());
    }

    [Fact]
    public void HandlesNonKeyedState_BySerializingAsJson() {
        var state = new { Id = 7 };
        _jsonSerializer.Serialize(Arg.Any<object>()).Returns("{\"Id\":7}");

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test message");

        var root = JsonDocument.Parse(result).RootElement;

        Assert.Equal(7, root.GetProperty("request").GetProperty("Id").GetInt32());
        Assert.Equal(state.GetType().Name, root.GetProperty("stateType").GetString());
        _jsonSerializer.Received(1).Serialize(state);
    }

    [Fact]
    public void TruncatesStackTrace_WhenExceedsMaxLength() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "error")
        };

        // Generate a real exception with a stack trace
        Exception exception;
        try { throw new InvalidOperationException("something went wrong"); }
        catch (Exception ex) { exception = ex; }

        var builder = new StructuredLogLineBuilder(
            _jsonSerializer, new StringBuilderPool(), "TestLogger",
            maxStackTraceLength: 50);

        var result = builder.Build(LogLevel.Error, new EventId(1),
            state, exception, (s, e) => "error");

        var doc = JsonDocument.Parse(result);
        var stackTrace = doc.RootElement.GetProperty("stackTrace").GetString()!;
        Assert.Contains("...[truncated]", stackTrace);
    }

    /// <summary>
    /// Truncation used to cut at a byte offset and append <c>...[truncated]}</c>, which lands
    /// mid-string and closes the object over an unterminated one — so the lines that lost content
    /// were also the lines Logs Insights could not read. Asserting the length and the closing brace,
    /// as this did, passes on both the broken and the fixed version; parsing is the assertion that
    /// separates them.
    /// </summary>
    [Fact]
    public void TruncatesOverallLogLine_AndTheResultIsStillParseable() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test"),
            new("bigData", new string('y', 5000))
        };

        var builder = new StructuredLogLineBuilder(
            _jsonSerializer, new StringBuilderPool(), "TestLogger",
            maxLogLineLength: 512);

        var result = builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test");

        Assert.True(result.Length <= 512, $"line was {result.Length} characters");

        var root = JsonDocument.Parse(result).RootElement;

        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    /// <summary>
    /// Whatever else is dropped, the message survives — it is written first for that reason.
    /// </summary>
    [Fact]
    public void TruncationKeepsTheMessage() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test"),
            new("a", new string('y', 2000)),
            new("b", new string('z', 2000))
        };

        var builder = new StructuredLogLineBuilder(
            _jsonSerializer, new StringBuilderPool(), "TestLogger",
            maxLogLineLength: 256);

        var result = builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "the important part");

        var root = JsonDocument.Parse(result).RootElement;

        Assert.Equal("the important part", root.GetProperty("message").GetString());
    }

    /// <summary>
    /// A line that fits carries no truncation marker, so a query for <c>truncated</c> finds only
    /// the lines that actually lost content.
    /// </summary>
    [Fact]
    public void AnUntruncatedLineIsNotMarkedTruncated() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "small"),
            new("userId", "user-123")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "small");

        Assert.False(JsonDocument.Parse(result).RootElement.TryGetProperty("truncated", out _));
    }

    [Fact]
    public void DoesNotTruncate_WhenUnderLimits() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "short message")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "short message");

        Assert.DoesNotContain("...[truncated]", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("short message", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void IncludesScopeProperties_WhenProvided() {
        var state = new List<KeyValuePair<string, object?>> {
            new("{OriginalFormat}", "test")
        };
        var scopeProperties = new List<KeyValuePair<string, object?>> {
            new("userId", "user-123"),
            new("sessionId", "sess-456"),
            new("requestId", "req-789")
        };

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test message", scopeProperties);

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal("user-123", root.GetProperty("userId").GetString());
        Assert.Equal("sess-456", root.GetProperty("sessionId").GetString());
        Assert.Equal("req-789", root.GetProperty("requestId").GetString());
    }
}
