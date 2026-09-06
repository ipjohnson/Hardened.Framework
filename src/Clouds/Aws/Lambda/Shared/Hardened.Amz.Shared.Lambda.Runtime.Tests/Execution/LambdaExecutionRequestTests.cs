using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Execution;

public class LambdaExecutionRequestTests {
    private static LambdaExecutionRequest CreateRequest(
        string method = "GET",
        string path = "/test",
        Stream? body = null,
        IDictionary<string, StringValues>? headers = null) {
        return new LambdaExecutionRequest(
            method,
            path,
            body ?? new MemoryStream(),
            headers ?? new Dictionary<string, StringValues>());
    }

    [Fact]
    public void Clone_PreservesMethod_WhenMethodParamIsNull() {
        var request = CreateRequest(method: "POST");

        var clone = request.Clone(
            method: null,
            path: "/new-path",
            headers: new Dictionary<string, StringValues>(),
            queryString: null!,
            cookies: null!);

        Assert.Equal("POST", clone.Method);
    }

    [Fact]
    public void Clone_PreservesPath_WhenPathParamIsNull() {
        var request = CreateRequest(path: "/original");

        var clone = request.Clone(
            method: "PUT",
            path: null,
            headers: new Dictionary<string, StringValues>(),
            queryString: null!,
            cookies: null!);

        Assert.Equal("/original", clone.Path);
    }

    [Fact]
    public void Clone_UsesProvidedMethodAndPath_WhenNonNull() {
        var request = CreateRequest(method: "GET", path: "/old");

        var clone = request.Clone(
            method: "DELETE",
            path: "/new",
            headers: new Dictionary<string, StringValues>(),
            queryString: null!,
            cookies: null!);

        Assert.Equal("DELETE", clone.Method);
        Assert.Equal("/new", clone.Path);
    }

    [Fact]
    public void Properties_ReturnConstructorValues() {
        var body = new MemoryStream(new byte[] { 1, 2, 3 });
        var headers = new Dictionary<string, StringValues> {
            { "Content-Type", new StringValues("application/json") }
        };

        var request = CreateRequest(method: "POST", path: "/api/data", body: body, headers: headers);

        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/data", request.Path);
        Assert.Same(body, request.Body);
        Assert.Same(headers, request.Headers);
        Assert.Equal("application/json", request.ContentType);
    }
}
