using System.Text.Json;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.Rie.Tests;

/// <summary>
/// The HTTP fixture inside the Lambda base image. Web-shaped, so the proxy response the
/// invocation returns is the whole observation.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class LambdaHttpImageTests : IClassFixture<LambdaHttpImageTests.Function> {
    private readonly Function _function;

    public LambdaHttpImageTests(Function function) {
        _function = function;
    }

    private static string Event(string method, string path, string body = "") => $$"""
        {"version":"2.0","rawPath":"{{path}}","rawQueryString":"",
         "headers":{"content-type":"application/json"},
         "requestContext":{"domainName":"apigateway.test","http":{"method":"{{method}}","path":"{{path}}","protocol":"HTTP/1.1","sourceIp":"203.0.113.7"} },
         "body":{{JsonSerializer.Serialize(body)}},"isBase64Encoded":false}
        """;

    [Fact]
    public async Task AGetIsRoutedAndItsPathTokenBound() {
        var result = await _function.Emulator.InvokeAsync(Event("GET", "/orders/o-1"), TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);

        using var proxy = JsonDocument.Parse(result.Body);

        Assert.Equal(200, proxy.RootElement.GetProperty("statusCode").GetInt32());

        using var order = JsonDocument.Parse(proxy.RootElement.GetProperty("body").GetString()!);

        Assert.Equal("o-1", order.RootElement.GetProperty("id").GetString());
        Assert.Equal(7, order.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task APostBindsItsBody() {
        var result = await _function.Emulator.InvokeAsync(
            Event("POST", "/orders", """{"id":"o-2","quantity":3}"""), TestContext.Current.CancellationToken);

        using var proxy = JsonDocument.Parse(result.Body);
        using var order = JsonDocument.Parse(proxy.RootElement.GetProperty("body").GetString()!);

        Assert.Equal("o-2", order.RootElement.GetProperty("id").GetString());
        Assert.Equal(3, order.RootElement.GetProperty("quantity").GetInt32());
    }

    /// <summary>
    /// A 404 is an answer, not a failed invocation: the caller is on an HTTP connection and gets the
    /// status the application chose, which is the web-shaped failure policy end to end.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedPathIsAnsweredWith404() {
        var result = await _function.Emulator.InvokeAsync(Event("GET", "/nothing-here"), TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);

        using var proxy = JsonDocument.Parse(result.Body);

        Assert.Equal(404, proxy.RootElement.GetProperty("statusCode").GetInt32());
    }

    public sealed class Function : IAsyncLifetime {
        public LambdaRuntimeInterfaceEmulator Emulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.RieLambdaHttp.SUT"),
            "Hardened.IntegrationTests.RieLambdaHttp.SUT");

        public async ValueTask InitializeAsync() => await Emulator.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Emulator.DisposeAsync();
    }
}
