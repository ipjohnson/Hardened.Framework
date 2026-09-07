using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.ApiGateway.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.ApiGateway.SUT.Tests;

/// <summary>
/// A web application deployed as a Lambda: ordinary verbs on a controller, reached through an API
/// Gateway payload.
///
/// <para>
/// This is the fixture the portability claim rests on. Nothing in the SUT names AWS - the verbs
/// bind <c>HardenedHttpModule</c>, and which module that is comes from the referenced runtime
/// package. The same handlers compile against Kestrel with a different reference and no edit.
/// </para>
/// </summary>
public class HttpFunctionTests : IDisposable {
    private readonly ServiceProvider _provider;

    public HttpFunctionTests() {
        _provider = new ApiGatewayTestApp().CreateServiceProvider(
            new EnvironmentImpl(null), null, builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private async Task<JsonElement> Invoke(string method, string path, string? body = null) {
        var payload = $$"""
            {
              "version":"2.0",
              "rawPath":"{{path}}",
              "headers":{"content-type":"application/json","accept":"application/json"},
              "requestContext":{
                "stage":"$default",
                "domainName":"api.example.com",
                "http":{"method":"{{method}}","path":"{{path}}","protocol":"HTTP/1.1","sourceIp":"203.0.113.7"}
              }
              {{(body == null ? "" : ",\"body\":" + JsonSerializer.Serialize(body))}}
            }
            """;

        var output = await _provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), new InvocationContext());

        return JsonDocument.Parse(new StreamReader(output).ReadToEnd()).RootElement.Clone();
    }

    // ------------------------------------------------------------------ routing

    [Fact]
    public async Task AGetReachesItsHandlerWithThePathToken() {
        var response = await Invoke("GET", "/orders/o-1");

        Assert.Equal(200, response.GetProperty("statusCode").GetInt32());

        using var body = JsonDocument.Parse(response.GetProperty("body").GetString()!);

        Assert.Equal("o-1", body.RootElement.GetProperty("id").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task APostBindsItsBody() {
        var response = await Invoke("POST", "/orders", """{"id":"o-2","quantity":3}""");

        using var body = JsonDocument.Parse(response.GetProperty("body").GetString()!);

        Assert.Equal("o-2", body.RootElement.GetProperty("id").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("quantity").GetInt32());
    }

    /// <summary>
    /// The verb is part of the route, so the same path under a different method is a different
    /// handler - and a void one answers without a body.
    /// </summary>
    [Fact]
    public async Task ADeleteOnTheSamePathReachesADifferentHandler() {
        var response = await Invoke("DELETE", "/orders/o-1");

        Assert.Equal(200, response.GetProperty("statusCode").GetInt32());
    }

    /// <summary>
    /// The 404 this transport spent a long time unable to send. The proxy response's status was a
    /// non-nullable int starting at zero, so the not-found handler never found it unset and every
    /// unmatched path came back as an empty 200.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedPathIsA404() {
        var response = await Invoke("GET", "/nothing-here");

        Assert.Equal(404, response.GetProperty("statusCode").GetInt32());
    }

    // ------------------------------------------------------------------ the family

    /// <summary>
    /// A web application answers rather than failing. The caller is on the other end of a
    /// connection, and a failed invocation would give them a 502 with nothing in it - which is why
    /// this family's adapter asks for Answer500 where every event adapter asks for Rethrow.
    /// </summary>
    [Fact]
    public void TheGatewayAdapterAnswersFailuresRatherThanRethrowing() {
        var adapter = Assert.IsType<ApiGatewayAdapter>(
            Assert.Single(_provider.GetServices<IPayloadAdapter>()));

        Assert.Equal(
            Hardened.Requests.Abstract.Execution.HostFailurePolicy.Answer500,
            adapter.FailurePolicy);
    }

    /// <summary>
    /// Verbs bound the adapter. Nothing in the SUT mentions API Gateway, SQS or Lambda.
    /// </summary>
    [Fact]
    public void TheVerbsRegisteredTheGatewayAdapter() {
        Assert.IsType<ApiGatewayAdapter>(Assert.Single(_provider.GetServices<IPayloadAdapter>()));
    }

    private sealed class InvocationContext : ILambdaContext {
        public string AwsRequestId => "integration";
        public IClientContext ClientContext => null!;
        public string FunctionName => "orders-api";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:orders-api";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/orders-api";
        public string LogStreamName => "stream";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
