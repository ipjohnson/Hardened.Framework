using System.Text.Json;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class ResponsePreludeTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ResponsePrelude_SerializesCorrectly() {
        var prelude = new ResponsePrelude {
            StatusCode = 200,
            Headers = new Dictionary<string, string> {
                { "Content-Type", "application/json" },
                { "X-Custom", "value" }
            },
            Cookies = new[] { "session=abc123", "theme=dark" }
        };

        var json = JsonSerializer.Serialize(prelude);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(200, root.GetProperty("statusCode").GetInt32());
        Assert.Equal("application/json",
            root.GetProperty("headers").GetProperty("Content-Type").GetString());
        Assert.Equal("value",
            root.GetProperty("headers").GetProperty("X-Custom").GetString());

        var cookies = root.GetProperty("cookies");
        Assert.Equal(2, cookies.GetArrayLength());
        Assert.Equal("session=abc123", cookies[0].GetString());
        Assert.Equal("theme=dark", cookies[1].GetString());
    }

    [Fact]
    public void ResponsePrelude_SerializesEmptyCollections() {
        var prelude = new ResponsePrelude {
            StatusCode = 404,
        };

        var json = JsonSerializer.Serialize(prelude);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(404, root.GetProperty("statusCode").GetInt32());
        Assert.Empty(root.GetProperty("headers").EnumerateObject().ToList());
        Assert.Empty(root.GetProperty("cookies").EnumerateArray().ToList());
    }

    [Fact]
    public void ResponsePrelude_Roundtrips() {
        var original = new ResponsePrelude {
            StatusCode = 301,
            Headers = new Dictionary<string, string> {
                { "Location", "https://example.com" }
            },
            Cookies = new[] { "redirect=true" }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ResponsePrelude>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(301, deserialized.StatusCode);
        Assert.Equal("https://example.com", deserialized.Headers["Location"]);
        Assert.Single(deserialized.Cookies);
    }

    [Fact]
    public void LambdaErrorResponse_SerializesCorrectly() {
        var error = new LambdaErrorResponse {
            ErrorMessage = "Something went wrong",
            ErrorType = "InvalidOperationException"
        };

        var json = JsonSerializer.Serialize(error);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Something went wrong", root.GetProperty("errorMessage").GetString());
        Assert.Equal("InvalidOperationException", root.GetProperty("errorType").GetString());
    }

    [Fact]
    public void LambdaErrorResponse_Roundtrips() {
        var original = new LambdaErrorResponse {
            ErrorMessage = "test error",
            ErrorType = "TestException"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<LambdaErrorResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("test error", deserialized.ErrorMessage);
        Assert.Equal("TestException", deserialized.ErrorType);
    }
}
