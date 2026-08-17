using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

/// <summary>
/// CORS headers are a security boundary: emitting <c>Access-Control-Allow-Origin</c> for an origin
/// that was never allowed hands that origin read access to authenticated responses. Each case is
/// asserted for what it emits and for what it must not.
///
/// <para>
/// Real request and response objects throughout, and a real chain. The previous version of this
/// file substituted all of them, which is the arrangement that let a broken retry filter pass
/// seventeen tests elsewhere in this repository.
/// </para>
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

    private static IExecutionContext Context(
        string method, string? origin, string? requestMethod = null, string? requestHeaders = null) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (origin != null) {
            headers[KnownHeaders.Origin] = origin;
        }

        if (requestMethod != null) {
            headers[KnownHeaders.Cors.AccessControlRequestMethod] = requestMethod;
        }

        if (requestHeaders != null) {
            headers[KnownHeaders.Cors.AccessControlRequestHeaders] = requestHeaders;
        }

        var request = new TestExecutionRequest(
            method, "/orders", "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Headers = headers
        };

        var services = new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    /// <summary>Runs the filter and reports whether the chain continued past it.</summary>
    private static async Task<bool> Run(CorsFilter filter, IExecutionContext context) {
        var continued = false;

        var chain = new ExecutionChain(
            new Func<IExecutionContext, IExecutionFilter>[] {
                _ => filter,
                _ => new Terminal(() => continued = true)
            },
            context);

        await chain.Next();

        return continued;
    }

    private sealed class Terminal : IExecutionFilter {
        private readonly Action _onRun;

        public Terminal(Action onRun) {
            _onRun = onRun;
        }

        public Task Execute(IExecutionChain chain) {
            _onRun();

            return Task.CompletedTask;
        }
    }

    /// <summary>A routing table that answers for one path under a fixed set of verbs.</summary>
    private sealed class Routes : IWebExecutionRequestHandlerProvider {
        private readonly string _path;
        private readonly HashSet<string> _methods;

        public Routes(string path, params string[] methods) {
            _path = path;
            _methods = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase);
        }

        public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
            if (!string.Equals(context.Request.Path, _path, StringComparison.Ordinal)) {
                return null;
            }

            if (_methods.Contains(context.Request.Method)) {
                return new RequestHandlerInfo(
                    Substitute.For<IExecutionRequestHandler>(), PathTokenCollection.Empty);
            }

            return RequestHandlerInfo.MethodNotAllowed(string.Join(", ", _methods));
        }
    }

    // ---------------------------------------------------------------- Vary

    /// <summary>
    /// Every response built by looking at <c>Origin</c> says so. Without it a shared cache may hand
    /// one origin's response - allow header included - to the next origin that asks, which is the
    /// most consequential thing this filter does.
    /// </summary>
    [Theory]
    [InlineData(Allowed)]
    [InlineData(Denied)]
    public async Task Execute_SetsVaryOriginWhicheverWayTheOriginIsDecided(string origin) {
        var context = Context("GET", origin);

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal("Origin", context.Response.Headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>A request that is not cross-origin is not varied on, and is not annotated.</summary>
    [Fact]
    public async Task Execute_LeavesARequestWithNoOriginEntirelyAlone() {
        var context = Context("GET", origin: null);

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.True(continued);
        Assert.Empty(context.Response.Headers);
    }

    /// <summary>An empty Origin header is not an origin.</summary>
    [Fact]
    public async Task Execute_LeavesAnEmptyOriginHeaderAlone() {
        var context = Context("GET", origin: "");

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.True(continued);
        Assert.Empty(context.Response.Headers);
    }

    // ------------------------------------------------------- actual requests

    [Fact]
    public async Task Execute_AnnotatesAnAllowedCrossOriginRequestAndContinues() {
        var context = Context("GET", Allowed);

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.True(continued);
        Assert.Equal(
            Allowed, context.Response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
    }

    [Fact]
    public async Task Execute_DoesNotAnnotateADisallowedOrigin() {
        var context = Context("GET", Denied);

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.True(continued);
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// The preflight-only headers are not put on an ordinary response. They mean nothing there;
    /// the previous version attached all three to every cross-origin response.
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotPutPreflightHeadersOnAnActualResponse() {
        var context = Context("GET", Allowed);

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowMethods));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowHeaders));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlMaxAge));
    }

    /// <summary>
    /// Without this, a cross-origin script can read only the safelisted headers - not a correlation
    /// id, not a pagination header, not <c>RateLimit-*</c>.
    /// </summary>
    [Fact]
    public async Task Execute_ExposesTheConfiguredResponseHeaders() {
        var config = ConfigAllowing(Allowed);

        config.ExposeHeader("X-Request-Id");
        config.ExposeHeader("X-Total-Count");

        var context = Context("GET", Allowed);

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            "X-Request-Id, X-Total-Count",
            context.Response.Headers[KnownHeaders.Cors.AccessControlExposeHeaders].ToString());
    }

    // ------------------------------------------------------------- preflight

    /// <summary>
    /// An <c>OPTIONS</c> with no <c>Access-Control-Request-Method</c> is not a preflight and must
    /// reach whatever answers <c>OPTIONS</c>. The previous version answered 204 to every
    /// <c>OPTIONS</c>, so such a handler was unreachable.
    /// </summary>
    [Fact]
    public async Task Execute_TreatsAnOptionsWithoutARequestMethodAsAnOrdinaryRequest() {
        var context = Context("OPTIONS", Allowed);

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.True(continued);
        Assert.Null(context.Response.Status);
    }

    /// <summary>A preflight is answered here and goes no further.</summary>
    [Fact]
    public async Task Execute_AnswersAPreflightWithoutContinuing() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.False(continued);
        Assert.Equal(204, context.Response.Status);
        Assert.False(context.Response.ShouldSerialize);
    }

    [Fact]
    public async Task Execute_AnswersAnAllowedPreflightWithTheCorsHeaders() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        var headers = context.Response.Headers;

        Assert.Equal(Allowed, headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.Equal("86400", headers[KnownHeaders.Cors.AccessControlMaxAge].ToString());
    }

    /// <summary>
    /// A refused preflight is still a 204, and carries no CORS headers. Their absence is what tells
    /// the browser not to send the real request.
    /// </summary>
    [Fact]
    public async Task Execute_AnswersAPreflightFromADeniedOriginWithNoCorsHeaders() {
        var context = Context("OPTIONS", Denied, requestMethod: "GET");

        var continued = await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.False(continued);
        Assert.Equal(204, context.Response.Status);
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// The requested headers are echoed rather than the whole configured set, which is what the
    /// specification asks for and keeps the header from growing with the configuration.
    /// </summary>
    [Fact]
    public async Task Execute_EchoesTheRequestedHeadersOnAnAllowedPreflight() {
        var context = Context(
            "OPTIONS", Allowed, requestMethod: "POST", requestHeaders: "Content-Type, Authorization");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal(
            "Content-Type, Authorization",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowHeaders].ToString());
    }

    /// <summary>
    /// Asking for a header that is not allowed fails the whole preflight. Echoing a subset would
    /// have the browser block the real request anyway, having been told the preflight succeeded.
    /// </summary>
    [Fact]
    public async Task Execute_RefusesAPreflightAskingForAnUnallowedHeader() {
        var context = Context(
            "OPTIONS", Allowed, requestMethod: "POST", requestHeaders: "X-Custom-Thing");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal(204, context.Response.Status);
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    [Fact]
    public async Task Execute_AllowsAnExplicitlyConfiguredRequestHeader() {
        var config = ConfigAllowing(Allowed);

        config.AllowHeader("X-Custom-Thing");

        var context = Context(
            "OPTIONS", Allowed, requestMethod: "POST", requestHeaders: "X-Custom-Thing");

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            Allowed,
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
    }

    // --------------------------------------------------- routing-aware verbs

    /// <summary>
    /// The advertised verbs come from the routing table, not from configuration. A path with only a
    /// <c>GET</c> must not advertise <c>DELETE</c>.
    /// </summary>
    [Fact]
    public async Task Execute_AdvertisesTheVerbsTheRouteActuallyHas() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(
            new CorsFilter(ConfigAllowing(Allowed), new[] { new Routes("/orders", "GET") }), context);

        Assert.Equal(
            "GET", context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString());
    }

    /// <summary>
    /// A verb the path does not have is refused, rather than advertised because configuration
    /// listed it.
    /// </summary>
    [Fact]
    public async Task Execute_RefusesAPreflightForAVerbTheRouteDoesNotHave() {
        var context = Context("OPTIONS", Allowed, requestMethod: "DELETE");

        await Run(
            new CorsFilter(ConfigAllowing(Allowed), new[] { new Routes("/orders", "GET", "POST") }),
            context);

        Assert.Equal(204, context.Response.Status);
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// A path no table recognises falls back to the configured list - "no route" is also what a
    /// request for static content looks like.
    /// </summary>
    [Fact]
    public async Task Execute_FallsBackToTheConfiguredVerbsForAnUnroutedPath() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(
            new CorsFilter(ConfigAllowing(Allowed), new[] { new Routes("/somewhere-else", "GET") }),
            context);

        Assert.Equal(
            "GET, POST, PUT, DELETE, OPTIONS",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString());
    }

    /// <summary>An application with no web routing at all still answers preflights.</summary>
    [Fact]
    public async Task Execute_FallsBackToTheConfiguredVerbsWhenThereIsNoRouting() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal(
            "GET, POST, PUT, DELETE, OPTIONS",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString());
    }

    /// <summary>
    /// Two tables can both recognise a path - one under other verbs, one under the requested one -
    /// and the preflight must advertise both, or a client is told a verb it may call is
    /// unavailable. The same case <c>WebExecutionHandlerService</c> merges <c>Allow</c> for on a
    /// 405.
    /// </summary>
    [Fact]
    public async Task Execute_MergesTheVerbsFromEveryTableThatRecognisesThePath() {
        var context = Context("OPTIONS", Allowed, requestMethod: "POST");

        await Run(
            new CorsFilter(
                ConfigAllowing(Allowed),
                new IWebExecutionRequestHandlerProvider[] {
                    new Routes("/orders", "GET"),
                    new Routes("/orders", "POST")
                }),
            context);

        var methods = context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString();

        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    /// <summary>
    /// A table that recognises the path but reports nothing allowed contributes nothing, rather
    /// than an empty entry in the advertised list.
    /// </summary>
    [Fact]
    public async Task Execute_IgnoresATableThatNamesNoAllowedVerbs() {
        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(
            new CorsFilter(
                ConfigAllowing(Allowed),
                new IWebExecutionRequestHandlerProvider[] {
                    new SilentRoutes("/orders"),
                    new Routes("/orders", "GET")
                }),
            context);

        Assert.Equal(
            "GET", context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString());
    }

    /// <summary>
    /// A preflight naming no verb is malformed and is refused rather than answered with the
    /// configured list, which would advertise verbs nobody asked about.
    /// </summary>
    [Fact]
    public async Task Execute_RefusesAPreflightThatNamesNoVerb() {
        var context = Context("OPTIONS", Allowed, requestMethod: "");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal(204, context.Response.Status);
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// A blank <c>Access-Control-Request-Headers</c> is no headers, not one header named "". A
    /// browser sends the header empty rather than omitting it in some cases, and treating that as a
    /// request for an unnamed header would fail every such preflight.
    /// </summary>
    [Fact]
    public async Task Execute_TreatsABlankRequestHeadersListAsNone() {
        var context = Context(
            "OPTIONS", Allowed, requestMethod: "GET", requestHeaders: "   ");

        await Run(new CorsFilter(ConfigAllowing(Allowed)), context);

        Assert.Equal(
            Allowed,
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowHeaders));
    }

    /// <summary>Recognises a path and reports no allowed verbs.</summary>
    private sealed class SilentRoutes : IWebExecutionRequestHandlerProvider {
        private readonly string _path;

        public SilentRoutes(string path) {
            _path = path;
        }

        public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) =>
            string.Equals(context.Request.Path, _path, StringComparison.Ordinal)
                ? new RequestHandlerInfo(null, PathTokenCollection.Empty)
                : null;
    }

    // ----------------------------------------------------------- credentials

    [Fact]
    public async Task Execute_EmitsAllowCredentialsWhenConfigured() {
        var config = ConfigAllowing(Allowed);

        config.AllowCredentials = true;

        var context = Context("GET", Allowed);

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            "true",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowCredentials].ToString());
    }

    /// <summary>
    /// The preflight has to say so too. A browser that is told credentials are allowed only on the
    /// actual response has already decided not to send them, so omitting it here defeats the
    /// setting entirely.
    /// </summary>
    [Fact]
    public async Task Execute_EmitsAllowCredentialsOnThePreflightAsWell() {
        var config = ConfigAllowing(Allowed);

        config.AllowCredentials = true;

        var context = Context("OPTIONS", Allowed, requestMethod: "GET");

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            "true",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowCredentials].ToString());
    }

    /// <summary>
    /// Credentials and a wildcard origin are not a valid pair and browsers reject it, so the filter
    /// emits neither the wildcard nor the credentials header rather than an unusable combination.
    /// </summary>
    [Fact]
    public async Task Execute_NeverPairsCredentialsWithAWildcardOrigin() {
        var config = new CorsConfiguration { AllowAnyOrigin = true, AllowCredentials = true };
        var context = Context("GET", Denied);

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            Denied, context.Response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.False(
            context.Response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowCredentials));
    }

    /// <summary>Any origin, without credentials, answers with the cacheable wildcard.</summary>
    [Fact]
    public async Task Execute_AnswersAnyOriginWithAWildcard() {
        var config = new CorsConfiguration { AllowAnyOrigin = true };
        var context = Context("GET", Denied);

        await Run(new CorsFilter(config), context);

        Assert.Equal(
            "*", context.Response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
    }
}
