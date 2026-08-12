using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Web.Lambda.Harness;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Harness.Tests;

/// <summary>
/// An HTTP request in, an API Gateway event out, the handler's response back onto the HTTP
/// response.
///
/// <para>
/// This is what makes a Lambda web application runnable behind Kestrel — locally, in a container,
/// in an integration test. Everything downstream of it believes it is serving API Gateway, so a
/// mistranslation here means the harness disagrees with production about what the application
/// received, and every test run against it is confidently wrong.
/// </para>
/// </summary>
public class RequestToLambdaServiceTests {

    /// <summary>
    /// The handler the harness invokes. Records the event it was handed and answers with whatever
    /// the test set, which is the only way to observe both directions of the translation.
    /// </summary>
    private class RecordingHandler : IApiGatewayV2Handler {
        public static APIGatewayHttpApiV2ProxyRequest? Received { get; private set; }

        public static ILambdaContext? ReceivedContext { get; private set; }

        public static APIGatewayHttpApiV2ProxyResponse Response { get; set; } = new();

        public static void Reset() {
            Received = null;
            ReceivedContext = null;
            Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 200,
                Body = "",
                Headers = new Dictionary<string, string>()
            };
        }

        public Task<APIGatewayHttpApiV2ProxyResponse> Invoke(
            APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context) {
            Received = request;
            ReceivedContext = context;

            return Task.FromResult(Response);
        }
    }

    private static async Task<HttpContext> Run(Action<HttpContext> configure) {
        RecordingHandler.Reset();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        configure(context);

        var service = new RequestToLambdaService<RecordingHandler>();

        await service.HandleRequest(context, () => Task.CompletedTask);

        return context;
    }

    private static APIGatewayHttpApiV2ProxyRequest Received =>
        RecordingHandler.Received ?? throw new InvalidOperationException("The handler was not invoked.");

    private static string ResponseBody(HttpContext context) {
        context.Response.Body.Position = 0;

        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task TheVerbBecomesTheEventsHttpMethod(string verb) {
        await Run(context => {
            context.Request.Method = verb;
            context.Request.Path = "/orders";
        });

        Assert.Equal(verb, Received.RequestContext.Http.Method);
    }

    /// <summary>
    /// Payload format 2.0 carries the path twice — once on the event and once inside the request
    /// context. Routing reads <c>rawPath</c>, so both have to be filled or the harness routes
    /// nowhere while production routes fine.
    /// </summary>
    [Fact]
    public async Task ThePathBecomesBothRawPathAndTheRequestContextPath() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/orders/42";
        });

        Assert.Equal("/orders/42", Received.RawPath);
        Assert.Equal("/orders/42", Received.RequestContext.Http.Path);
    }

    [Fact]
    public async Task TheProtocolIsCarriedIntoTheRequestContext() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/";
            context.Request.Protocol = "HTTP/1.1";
        });

        Assert.Equal("HTTP/1.1", Received.RequestContext.Http.Protocol);
    }

    [Fact]
    public async Task RequestHeadersBecomeEventHeaders() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/";
            context.Request.Headers["X-Tenant"] = "acme";
            context.Request.Headers["Authorization"] = "Bearer token";
        });

        Assert.Equal("acme", Received.Headers["X-Tenant"]);
        Assert.Equal("Bearer token", Received.Headers["Authorization"]);
    }

    /// <summary>
    /// Kestrel keeps repeated headers as separate values; API Gateway joins them on a comma before
    /// the function sees them. Joining here is what keeps the two agreeing.
    /// </summary>
    [Fact]
    public async Task ARepeatedRequestHeaderIsJoinedTheWayApiGatewayJoinsIt() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/";
            context.Request.Headers["Accept-Encoding"] =
                new Microsoft.Extensions.Primitives.StringValues(["gzip", "br"]);
        });

        Assert.Equal("gzip,br", Received.Headers["Accept-Encoding"]);
    }

    [Fact]
    public async Task TheUserAgentIsCarriedIntoTheRequestContext() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/";
            context.Request.Headers.UserAgent = "curl/8.4.0";
        });

        Assert.Equal("curl/8.4.0", Received.RequestContext.Http.UserAgent);
    }

    [Fact]
    public async Task QueryParametersBecomeEventQueryStringParameters() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/search";
            context.Request.QueryString = new QueryString("?q=widget&page=2");
        });

        Assert.Equal("widget", Received.QueryStringParameters["q"]);
        Assert.Equal("2", Received.QueryStringParameters["page"]);
    }

    /// <summary>
    /// <c>rawQueryString</c> has no leading <c>?</c> in a real event. Leaving one on makes the
    /// first parameter name start with <c>?</c> for anything that parses it.
    /// </summary>
    [Fact]
    public async Task TheRawQueryStringHasNoLeadingQuestionMark() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/search";
            context.Request.QueryString = new QueryString("?q=widget");
        });

        Assert.Equal("q=widget", Received.RawQueryString);
    }

    [Fact]
    public async Task ARequestWithNoQueryStringCarriesAnEmptyRawQueryString() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/search";
        });

        Assert.Equal("", Received.RawQueryString);
        Assert.Empty(Received.QueryStringParameters);
    }

    [Fact]
    public async Task TheRequestBodyBecomesTheEventBody() {
        await Run(context => {
            context.Request.Method = "POST";
            context.Request.Path = "/orders";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"sku":"ABC"}"""));
        });

        Assert.Equal("""{"sku":"ABC"}""", Received.Body);
    }

    [Fact]
    public async Task ARequestWithNoBodyCarriesAnEmptyBody() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/orders";
        });

        Assert.Equal("", Received.Body);
    }

    /// <summary>
    /// The context is manufactured, not real, but a handler reading <c>FunctionName</c> has to get
    /// something rather than a null reference.
    /// </summary>
    [Fact]
    public async Task TheHandlerIsGivenALambdaContextNamedForTheApplication() {
        await Run(context => {
            context.Request.Method = "GET";
            context.Request.Path = "/";
        });

        Assert.NotNull(RecordingHandler.ReceivedContext);
        Assert.Equal(nameof(RecordingHandler), RecordingHandler.ReceivedContext!.FunctionName);
    }

    [Fact]
    public async Task TheEventStatusBecomesTheHttpStatus() {
        var context = await Run(httpContext => {
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            RecordingHandler.Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 404,
                Body = "",
                Headers = new Dictionary<string, string>()
            };
        });

        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task EventHeadersBecomeHttpResponseHeaders() {
        var context = await Run(httpContext => {
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            RecordingHandler.Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 200,
                Body = "",
                Headers = new Dictionary<string, string> {
                    ["Content-Type"] = "application/json",
                    ["X-Request-Id"] = "req-9"
                }
            };
        });

        Assert.Equal("application/json", context.Response.Headers["Content-Type"]);
        Assert.Equal("req-9", context.Response.Headers["X-Request-Id"]);
    }

    [Fact]
    public async Task ATextEventBodyIsWrittenToTheHttpResponse() {
        var context = await Run(httpContext => {
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            RecordingHandler.Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 200,
                Body = """{"ok":true}""",
                Headers = new Dictionary<string, string>()
            };
        });

        Assert.Equal("""{"ok":true}""", ResponseBody(context));
    }

    /// <summary>
    /// A base64 body is decoded before it goes on the wire. Writing the base64 text instead would
    /// hand the caller an image, PDF or gzip stream that no client can read, under a 200.
    /// </summary>
    [Fact]
    public async Task ABase64EventBodyIsDecodedBeforeItIsWritten() {
        var payload = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF };

        var context = await Run(httpContext => {
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            RecordingHandler.Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 200,
                Body = Convert.ToBase64String(payload),
                IsBase64Encoded = true,
                Headers = new Dictionary<string, string>()
            };
        });

        context.Response.Body.Position = 0;

        using var written = new MemoryStream();
        await context.Response.Body.CopyToAsync(written, TestContext.Current.CancellationToken);

        Assert.Equal(payload, written.ToArray());
    }

    /// <summary>
    /// The whole point of the harness: a request goes in as HTTP, becomes an event, and the
    /// event's response comes back out as HTTP with nothing lost in either direction.
    /// </summary>
    [Fact]
    public async Task AnHttpRequestRoundTripsThroughTheEventAndBack() {
        var context = await Run(httpContext => {
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = "/orders/42";
            httpContext.Request.QueryString = new QueryString("?notify=true");
            httpContext.Request.Headers["X-Tenant"] = "acme";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"sku":"ABC"}"""));

            RecordingHandler.Response = new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 201,
                Body = """{"id":42}""",
                Headers = new Dictionary<string, string> { ["Location"] = "/orders/42" }
            };
        });

        Assert.Equal("POST", Received.RequestContext.Http.Method);
        Assert.Equal("/orders/42", Received.RawPath);
        Assert.Equal("true", Received.QueryStringParameters["notify"]);
        Assert.Equal("acme", Received.Headers["X-Tenant"]);
        Assert.Equal("""{"sku":"ABC"}""", Received.Body);

        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("/orders/42", context.Response.Headers["Location"]);
        Assert.Equal("""{"id":42}""", ResponseBody(context));
    }
}
