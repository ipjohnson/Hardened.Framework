using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Xunit;
using Hardened.Aws.Lambda.ApiGateway;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// The two halves the conformance suite does not reach: what the peek recognises, and what comes
/// out the far end.
/// </summary>
public class ApiGatewayAdapterTests {
    private readonly ApiGatewayAdapter _adapter = new();

    private bool Handles(string json) {
        using var payload = new LambdaPayload(Encoding.UTF8.GetBytes(json));

        return _adapter.Handles(payload.Json);
    }

    // ------------------------------------------------------------------ the peek

    [Fact]
    public void RecognisesPayloadFormatTwo() {
        Assert.True(Handles("""
            {"version":"2.0","rawPath":"/orders","requestContext":{"http":{"method":"GET"}}}
            """));
    }

    /// <summary>
    /// The distinction the diagnostic exists for. Format 1.0 puts the method on the root and its
    /// requestContext has no http object, so the adapter that names the v2 type must not claim it -
    /// which is what happened before, and the function failed on a null RequestContext.Http.
    /// </summary>
    [Fact]
    public void DeclinesPayloadFormatOne() {
        Assert.False(Handles("""
            {"resource":"/orders","httpMethod":"GET","path":"/orders",
             "requestContext":{"httpMethod":"GET","path":"/orders","stage":"prod"}}
            """));
    }

    [Theory]
    [InlineData("""{"Records":[{"eventSource":"aws:sqs","body":"{}"}]}""")]
    [InlineData("""{"Records":[{"EventSource":"aws:sns"}]}""")]
    [InlineData("""{"widgetContext":{"widgetId":"w1"}}""")]
    [InlineData("""{"orderId":"abc","quantity":2}""")]
    [InlineData("{}")]
    public void DeclinesEverythingElse(string json) {
        Assert.False(Handles(json));
    }

    /// <summary>
    /// A payload that is not an object at all. Nothing sends one, but the lookup has to answer
    /// rather than throw, because the adapter is asked before anything has vouched for the shape.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void DeclinesAPayloadThatIsNotAnObject(string json) {
        Assert.False(Handles(json));
    }

    /// <summary>
    /// A caller's own payload with a requestContext of its own, but no http inside it. Declining is
    /// what sends it to the direct-invoke adapter, where it belongs.
    /// </summary>
    [Fact]
    public void DeclinesARequestContextThatIsNotTheGatewaysOwn() {
        Assert.False(Handles("""{"requestContext":{"correlationId":"abc","source":"billing"}}"""));
    }

    /// <summary>
    /// An "http" nested deeper inside the caller's own requestContext is not the gateway's. Only a
    /// direct property of requestContext counts, which is what the depth check is for.
    /// </summary>
    [Fact]
    public void DeclinesAnHttpBuriedDeeperInsideSomeoneElsesContext() {
        Assert.False(Handles(
            """{"requestContext":{"upstream":{"http":{"method":"GET"}},"source":"billing"}}"""));
    }

    // ------------------------------------------------------------------ the request

    [Fact]
    public void DecodesABase64Body() {
        var request = _adapter.CreateRequest(Event(body: "eyJpZCI6MX0=", base64: true), TestLambdaContext.Instance);

        using var reader = new StreamReader(request.Body);

        Assert.Equal("""{"id":1}""", reader.ReadToEnd());
    }

    [Fact]
    public void ReadsAPlainBodyAsItStands() {
        var request = _adapter.CreateRequest(Event(body: """{"id":1}"""), TestLambdaContext.Instance);

        using var reader = new StreamReader(request.Body);

        Assert.Equal("""{"id":1}""", reader.ReadToEnd());
    }

    [Fact]
    public void AnAbsentBodyIsEmptyRatherThanNull() {
        var request = _adapter.CreateRequest(Event(), TestLambdaContext.Instance);

        Assert.NotNull(request.Body);
        Assert.Equal(0, request.Body.Length);
    }

    // ------------------------------------------------------------------ the response

    [Fact]
    public async Task AnUnsetStatusBecomesTwoHundred() {
        Assert.Equal(200, (await Answer(_ => { })).StatusCode);
    }

    /// <summary>
    /// The 404 this transport could not send. The status was backed by a non-nullable int, so the
    /// not-found handler never found it unset, and every unmatched path came back as an empty 200 -
    /// while the streaming transport returned 404 for the same application.
    /// </summary>
    [Fact]
    public async Task AStatusTheHandlerSetSurvives() {
        Assert.Equal(404, (await Answer(r => r.Status = 404)).StatusCode);
    }

    [Fact]
    public async Task HeadersReachTheGateway() {
        var proxy = await Answer(r => r.Headers.Set("X-Trace", "abc"));

        Assert.Equal("abc", proxy.Headers["X-Trace"]);
    }

    [Fact]
    public async Task CookiesGoInTheirOwnArrayRatherThanAHeader() {
        var proxy = await Answer(r => r.Cookies.Append("session", "abc123"));

        Assert.Contains(proxy.Cookies, c => c.StartsWith("session=abc123"));
        Assert.DoesNotContain(proxy.Headers, h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoCookiesMeansAnEmptyArray() {
        Assert.Empty((await Answer(_ => { })).Cookies);
    }

    [Fact]
    public async Task ABinaryBodyIsBase64Encoded() {
        var proxy = await Answer(r => {
            r.IsBinary = true;
            r.Body.Write("hardened"u8);
        });

        Assert.True(proxy.IsBase64Encoded);
        Assert.Equal("hardened", Encoding.UTF8.GetString(Convert.FromBase64String(proxy.Body)));
    }

    /// <summary>
    /// A non-binary body that is not valid UTF-8 gets the replacement character, not a throw.
    /// </summary>
    /// <remarks>
    /// Worth pinning because the response used to go out through
    /// <c>APIGatewayHttpApiV2ProxyResponse.Body</c>, a string, so the substitution was
    /// <c>Encoding.UTF8.GetString</c>'s. Writing the bytes straight into the payload hands that
    /// decision to the JSON writer's encoder instead, and this asserts the two agree - a handler
    /// that writes a stray byte without setting IsBinary still answers rather than failing the
    /// invocation.
    /// </remarks>
    [Fact]
    public async Task InvalidUtf8InATextBodyIsReplacedRatherThanThrown() {
        var proxy = await Answer(r => r.Body.Write([0x41, 0xFF, 0x42]));

        Assert.Equal("A\uFFFDB", proxy.Body);
    }

    [Fact]
    public async Task ATextBodyIsNotEncoded() {
        var proxy = await Answer(r => r.Body.Write("""{"ok":true}"""u8));

        Assert.False(proxy.IsBase64Encoded);
        Assert.Equal("""{"ok":true}""", proxy.Body);
    }

    // ------------------------------------------------------------------ helpers

    private static LambdaPayload Event(string? body = null, bool base64 = false) {
        var proxy = new APIGatewayHttpApiV2ProxyRequest {
            RawPath = "/orders",
            Version = "2.0",
            Body = body,
            IsBase64Encoded = base64,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = "POST", Path = "/orders" }
            }
        };

        return new LambdaPayload(JsonSerializer.SerializeToUtf8Bytes(
            proxy, TestSerializerContext.Default.APIGatewayHttpApiV2ProxyRequest));
    }

    /// <summary>Runs the response half: build one, let the caller write to it, read the payload.</summary>
    private async Task<APIGatewayHttpApiV2ProxyResponse> Answer(Action<ApiGatewayResponse> write) {
        var response = (ApiGatewayResponse)_adapter.CreateResponse(new MemoryStream());

        write(response);

        var output = new MemoryStream();

        await _adapter.WriteResponse(new ResponseOnlyContext(response), output);

        output.Position = 0;

        return JsonSerializer.Deserialize(
            output, TestSerializerContext.Default.APIGatewayHttpApiV2ProxyResponse)!;
    }
}
