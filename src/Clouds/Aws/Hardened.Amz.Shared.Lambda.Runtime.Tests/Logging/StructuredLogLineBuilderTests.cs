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

    [Fact]
    public void HandlesNonKeyedState_BySerializingAsJson() {
        var state = "just a plain string";
        _jsonSerializer.Serialize(state).Returns("\"just a plain string\"");

        var result = _builder.Build(LogLevel.Information, new EventId(1),
            state, null, (s, e) => "test message");

        Assert.Contains("\"request\":", result);
        Assert.Contains("\"stateType\":\"String\"", result);
        _jsonSerializer.Received(1).Serialize(state);
    }
}
