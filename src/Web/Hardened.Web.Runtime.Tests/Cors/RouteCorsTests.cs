using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

/// <summary>
/// The middleware once the build has registered <see cref="CorsManifest"/>: it answers preflights
/// with the policy of the route asked about, and leaves every actual request to that route.
/// </summary>
/// <remarks>
/// Reached through <c>HardenedWebModule</c> and its startup service, as
/// <c>CorsStartupServiceTests</c> reaches it, because the mode is decided there.
/// </remarks>
public class RouteCorsTests
{
    private const string Allowed = "https://app.example.com";
    private const string Partner = "https://partner.example.com";

    private sealed class Partners;

    /// <summary>A routing table with one GET route per path, each carrying its metadata.</summary>
    private sealed class Routes : IWebExecutionRequestHandlerProvider
    {
        private readonly Dictionary<string, IExecutionRequestHandler> _handlers = new();

        public Routes Get(string path, params object[] metadata)
        {
            var handler = Substitute.For<IExecutionRequestHandler>();

            handler.HandlerInfo.Returns(
                new ExecutionRequestHandlerInfo(
                    path,
                    "GET",
                    typeof(RouteCorsTests),
                    "Handle",
                    metadata: metadata
                )
            );

            _handlers[path] = handler;

            return this;
        }

        public RequestHandlerInfo? GetExecutionRequestHandler(
            IExecutionContext context,
            ref PathTokenCollection pathTokens
        )
        {
            if (!_handlers.TryGetValue(context.Request.Path, out var handler))
            {
                return null;
            }

            return string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                ? new RequestHandlerInfo(handler)
                : RequestHandlerInfo.MethodNotAllowed("GET");
        }
    }

    /// <summary>
    /// What a module's generated code registers, declared in this assembly, which is also where
    /// every handler above is declared.
    /// </summary>
    private sealed class ModuleDeclarations : IApplicationFilterDeclarations
    {
        public ModuleDeclarations(params object[] declared)
        {
            Declared = declared;
        }

        public IReadOnlyList<object> Declared { get; }
    }

    /// <summary>
    /// Starts an application over <paramref name="routes"/>, and returns the filter the CORS
    /// startup service put in the middleware chain.
    /// </summary>
    /// <remarks>
    /// The default policy allows <see cref="Allowed"/>, and the <see cref="Partners"/> policy
    /// allows <see cref="Partner"/> only.
    /// </remarks>
    private static async Task<IExecutionFilter> Installed(
        Routes routes,
        bool routesDeclare = true,
        IApplicationFilterDeclarations? module = null
    )
    {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        var middleware = Substitute.For<IMiddlewareService>();
        Func<IExecutionContext, IExecutionFilter>? installed = null;

        middleware.Use(Arg.Do<Func<IExecutionContext, IExecutionFilter>>(func => installed = func));

        var defaultPolicy = new CorsConfiguration();

        defaultPolicy.AllowOrigin(Allowed);

        services.AddSingleton(defaultPolicy);
        services.AddSingleton(middleware);
        services.AddSingleton<IWebExecutionRequestHandlerProvider>(routes);
        services.AddCorsPolicy<Partners>(policy => policy.AllowOrigin(Partner));

        if (routesDeclare)
        {
            services.AddSingleton<CorsManifest>();
        }

        if (module != null)
        {
            services.AddSingleton(module);
        }

        var provider = services.BuildServiceProvider();

        foreach (var startupService in provider.GetServices<IStartupService>())
        {
            await startupService.Startup(provider);
        }

        Assert.NotNull(installed);

        return installed!(Substitute.For<IExecutionContext>());
    }

    private static IExecutionContext Request(
        string method,
        string path,
        string origin,
        string? requestMethod = null
    )
    {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            [KnownHeaders.Origin] = origin,
        };

        if (requestMethod != null)
        {
            headers[KnownHeaders.Cors.AccessControlRequestMethod] = requestMethod;
        }

        var request = new TestExecutionRequest(
            method,
            path,
            "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())
        )
        {
            Headers = headers,
        };

        var services = new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );
    }

    private static IExecutionContext Preflight(string path, string origin) =>
        Request("OPTIONS", path, origin, requestMethod: "GET");

    /// <summary>Runs the filter, and reports whether the chain continued past it.</summary>
    private static async Task<bool> Run(IExecutionFilter filter, IExecutionContext context)
    {
        var continued = false;

        var chain = new ExecutionChain(
            new Func<IExecutionContext, IExecutionFilter>[]
            {
                _ => filter,
                _ => new Terminal(() => continued = true),
            },
            context
        );

        await chain.Next();

        return continued;
    }

    private sealed class Terminal : IExecutionFilter
    {
        private readonly Action _onRun;

        public Terminal(Action onRun)
        {
            _onRun = onRun;
        }

        public Task Execute(IExecutionChain chain)
        {
            _onRun();

            return Task.CompletedTask;
        }
    }

    private static string? AllowOrigin(IExecutionContext context) =>
        context.Response.Headers.TryGetValue(
            KnownHeaders.Cors.AccessControlAllowOrigin,
            out var value
        )
            ? value.ToString()
            : null;

    /// <summary>
    /// Not marked here, not even with <c>Vary</c>, because the route's own filter decides it. A
    /// route that declares nothing is therefore answered with no CORS headers at all.
    /// </summary>
    [Fact]
    public async Task AnActualRequestIsLeftToItsRoute()
    {
        var filter = await Installed(new Routes().Get("/plain"));
        var context = Request("GET", "/plain", Allowed);

        Assert.True(await Run(filter, context));
        Assert.Null(AllowOrigin(context));
        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.Vary));
    }

    [Fact]
    public async Task APreflightForARouteThatDeclaresCorsIsAnswered()
    {
        var filter = await Installed(new Routes().Get("/declared", new CorsAttribute()));
        var context = Preflight("/declared", Allowed);

        Assert.False(await Run(filter, context));
        Assert.Equal(204, context.Response.Status);
        Assert.Equal(Allowed, AllowOrigin(context));
        Assert.Equal(
            "GET",
            context.Response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString()
        );
    }

    /// <summary>
    /// Refused as every preflight is: a 204 that lacks the headers, which tells the browser not to
    /// send the real request.
    /// </summary>
    [Fact]
    public async Task APreflightForARouteThatDeclaresNoneIsRefused()
    {
        var filter = await Installed(
            new Routes().Get("/declared", new CorsAttribute()).Get("/plain")
        );
        var context = Preflight("/plain", Allowed);

        Assert.False(await Run(filter, context));
        Assert.Equal(204, context.Response.Status);
        Assert.Null(AllowOrigin(context));
    }

    /// <summary>
    /// With CORS on the whole application, a path no table recognises is answered with the
    /// configured verbs, because static content looks like that. Once routes declare CORS, no
    /// route means no declaration.
    /// </summary>
    [Fact]
    public async Task APreflightForAPathNoRouteHasIsRefused()
    {
        var filter = await Installed(new Routes().Get("/declared", new CorsAttribute()));
        var context = Preflight("/nowhere", Allowed);

        Assert.False(await Run(filter, context));
        Assert.Null(AllowOrigin(context));
    }

    [Fact]
    public async Task APreflightIsAnsweredWithTheModulesDeclaration()
    {
        var filter = await Installed(
            new Routes().Get("/plain"),
            module: new ModuleDeclarations(new CorsAttribute())
        );
        var context = Preflight("/plain", Allowed);

        await Run(filter, context);

        Assert.Equal(Allowed, AllowOrigin(context));
    }

    [Fact]
    public async Task APreflightIsAnsweredWithTheRoutesNamedPolicy()
    {
        var routes = new Routes().Get("/partners", new CorsAttribute<Partners>());
        var filter = await Installed(routes);

        var partner = Preflight("/partners", Partner);
        var allowedByTheDefault = Preflight("/partners", Allowed);

        await Run(filter, partner);
        await Run(filter, allowedByTheDefault);

        Assert.Equal(Partner, AllowOrigin(partner));
        Assert.Null(AllowOrigin(allowedByTheDefault));
    }

    /// <summary>
    /// The nearest declaration is the method's, which comes first in the handler's metadata, and it
    /// beats the module's.
    /// </summary>
    [Fact]
    public async Task TheRoutesOwnDeclarationBeatsTheModules()
    {
        var filter = await Installed(
            new Routes().Get("/partners", new CorsAttribute<Partners>()),
            module: new ModuleDeclarations(new CorsAttribute())
        );
        var context = Preflight("/partners", Allowed);

        await Run(filter, context);

        Assert.Null(AllowOrigin(context));
    }

    /// <summary>
    /// An application whose build registered no manifest keeps CORS on every request, which is
    /// what it had before routes could declare it.
    /// </summary>
    [Fact]
    public async Task WithoutTheManifestEveryRouteIsCovered()
    {
        var filter = await Installed(new Routes().Get("/plain"), routesDeclare: false);

        var actual = Request("GET", "/plain", Allowed);
        var preflight = Preflight("/plain", Allowed);

        Assert.True(await Run(filter, actual));
        await Run(filter, preflight);

        Assert.Equal(Allowed, AllowOrigin(actual));
        Assert.Equal(Allowed, AllowOrigin(preflight));
    }
}
