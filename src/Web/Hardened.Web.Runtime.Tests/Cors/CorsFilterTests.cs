using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Cors;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

/// <summary>
/// CORS headers are a security boundary: emitting Access-Control-Allow-Origin for an origin
/// that was never allowed hands that origin read access to authenticated responses. Each
/// case is therefore asserted for what it does emit and what it must not.
/// </summary>
public class CorsFilterTests {

    private const string Allowed = "https://app.example.com";
    private const string Denied = "https://evil.example.com";

    private static CorsConfiguration ConfigAllowing(params string[] origins) {
        var config = new CorsConfiguration();
        foreach (var origin in origins) {
            config.AllowOrigin(origin);
        }
        return config;
    }

    private static (IExecutionChain chain, IDictionary<string, StringValues> responseHeaders, IExecutionResponse response)
        Chain(string method, string? origin) {
        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();

        var requestHeaders = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        if (origin is not null) {
            requestHeaders["Origin"] = origin;
        }

        var responseHeaders = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        request.Method.Returns(method);
        request.Headers.Returns(requestHeaders);
        response.Headers.Returns(responseHeaders);
        context.Request.Returns(request);
        context.Response.Returns(response);
        chain.Context.Returns(context);

        return (chain, responseHeaders, response);
    }

    [Fact]
    public async Task AllowedOriginReceivesCorsHeaders() {
        var (chain, headers, _) = Chain("GET", Allowed);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        Assert.Equal(Allowed, headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.False(StringValues.IsNullOrEmpty(headers[KnownHeaders.Cors.AccessControlAllowHeaders]));
        Assert.False(StringValues.IsNullOrEmpty(headers[KnownHeaders.Cors.AccessControlAllowMethods]));
        Assert.Equal("86400", headers[KnownHeaders.Cors.AccessControlMaxAge].ToString());
    }

    [Fact]
    public async Task DisallowedOriginReceivesNoCorsHeaders() {
        var (chain, headers, _) = Chain("GET", Denied);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task RequestWithoutAnOriginReceivesNoCorsHeaders() {
        var (chain, headers, _) = Chain("GET", origin: null);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task EmptyOriginHeaderReceivesNoCorsHeaders() {
        var (chain, headers, _) = Chain("GET", "");

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task NonPreflightRequestContinuesDownTheChain() {
        var (chain, _, _) = Chain("GET", Allowed);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        await chain.Received(1).Next();
    }

    [Fact]
    public async Task PreflightShortCircuitsWith204AndDoesNotContinue() {
        var (chain, _, response) = Chain("OPTIONS", Allowed);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        response.Received().Status = 204;
        response.Received().ShouldSerialize = false;
        await chain.DidNotReceive().Next();
    }

    [Fact]
    public async Task PreflightFromAnAllowedOriginStillCarriesCorsHeaders() {
        var (chain, headers, _) = Chain("OPTIONS", Allowed);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        Assert.Equal(Allowed, headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
    }

    /// <summary>
    /// A preflight from a denied origin is still answered 204, but without the CORS headers,
    /// so the browser rejects the actual request. The important half of that is the absence
    /// of Access-Control-Allow-Origin.
    /// </summary>
    [Fact]
    public async Task PreflightFromADeniedOriginIsAnsweredWithoutCorsHeaders() {
        var (chain, headers, response) = Chain("OPTIONS", Denied);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        response.Received().Status = 204;
        Assert.Empty(headers);
    }

    [Fact]
    public async Task MethodMatchingForPreflightIsCaseInsensitive() {
        var (chain, _, response) = Chain("options", Allowed);

        await new CorsFilter(ConfigAllowing(Allowed)).Execute(chain);

        response.Received().Status = 204;
        await chain.DidNotReceive().Next();
    }

    [Fact]
    public async Task ConfiguredMethodsAndHeadersAreEmittedVerbatim() {
        var config = ConfigAllowing(Allowed);
        config.AllowedMethods = "GET, POST";
        config.AllowedHeaders = "Authorization";
        config.MaxAgeSec = 60;

        var (chain, headers, _) = Chain("GET", Allowed);

        await new CorsFilter(config).Execute(chain);

        Assert.Equal("GET, POST", headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString());
        Assert.Equal("Authorization", headers[KnownHeaders.Cors.AccessControlAllowHeaders].ToString());
        Assert.Equal("60", headers[KnownHeaders.Cors.AccessControlMaxAge].ToString());
    }
}
