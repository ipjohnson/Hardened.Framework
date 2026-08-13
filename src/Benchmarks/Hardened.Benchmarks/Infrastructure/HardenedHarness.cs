using Hardened.Benchmarks.Contracts;
using Hardened.Benchmarks.Sut;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// Builds configured Hardened service providers.
///
/// Each deployment gets its own provider. <c>MiddlewareService</c> is a singleton holding a plain
/// <c>List</c> of filters, and both <c>AspNetCoreExtensions.UseHardened</c> and the transport-free
/// bootstrap append the web filter to it. Sharing one provider between the two harnesses would
/// register that filter twice and silently run the whole routing and handler chain twice per
/// request — a benchmark that still produces a valid-looking response while measuring double the
/// work.
/// </summary>
internal static class HardenedAppFactory {

    public static ServiceProvider BuildProvider() {
        var services = new ServiceCollection();

        // No providers: logging is registered because the framework requires it, but anything
        // that actually writes would be measuring the console rather than the pipeline. The
        // ASP.NET harness does the same, so neither side is charged for output.
        services.AddLogging(builder => builder.ClearProviders());

        // Pinned rather than inherited from the machine, so a HARDENED_ENVIRONMENT set in a
        // shell cannot quietly change what is being measured.
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("production"));

        // Registered by hand, identically to the ASP.NET side, so that resolving it costs the
        // same in both containers.
        services.AddTransient<ISumService, SumService>();

        new BenchmarkApplication().PopulateServiceCollection(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Runs the registered startup services. CORS and the filter registry populate themselves
    /// here, so a provider that skips this step routes correctly but runs a different filter set
    /// than a real application would.
    /// </summary>
    public static void RunStartup(IServiceProvider provider) {
        foreach (var startupService in provider.GetServices<IStartupService>()) {
            startupService.Startup(provider).GetAwaiter().GetResult();
        }
    }
}

/// <summary>
/// Hardened with no transport at all: an execution context constructed directly, handed to the
/// middleware chain. This is the shape Hardened runs in on Lambda and on any other non-ASP.NET
/// compute, and it is the floor the other measurements are read against.
/// </summary>
public sealed class HardenedNativeHarness : IPipelineHarness {
    private readonly ServiceProvider _provider;
    private readonly IMiddlewareService _middleware;

    public string Name => "hardened-native";

    public HardenedNativeHarness() {
        _provider = HardenedAppFactory.BuildProvider();
        HardenedAppFactory.RunStartup(_provider);

        // The transport-free equivalent of what UseHardened does for ASP.NET, and of what
        // WebTestingAttribute does for the xUnit harness.
        var handler = _provider.GetRequiredService<IWebExecutionHandlerService>();
        _middleware = _provider.GetRequiredService<IMiddlewareService>();
        _middleware.Use(_ => handler);
    }

    public IServiceProvider Provider => _provider;

    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>
    /// Builds the execution context for a scenario. Public so that
    /// <c>ContextConstructionBenchmarks</c> can measure this portion against the ASP.NET
    /// equivalent using the same code the pipeline benchmark runs, rather than a copy of it that
    /// could drift.
    /// </summary>
    public TestExecutionContext CreateContext(
        RequestScenario scenario, IServiceScope scope, MemoryStream responseBody) {
        var headers = new Dictionary<string, StringValues>();

        foreach (var header in scenario.Headers) {
            headers[header.Key] = header.Value;
        }

        if (scenario.ContentType is not null) {
            headers["Content-Type"] = scenario.ContentType;
        }

        var request = new TestExecutionRequest(
            scenario.Method,
            scenario.Path,
            null,
            ParseQueryString(scenario.QueryString)) {
            Headers = headers,
            Body = scenario.Body is null ? Stream.Null : new MemoryStream(scenario.Body, false)
        };

        var response = new TestExecutionResponse(responseBody) {
            Headers = new Dictionary<string, StringValues>()
        };

        return new TestExecutionContext(
            _provider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            response,
            CancellationToken.None);
    }

    public async Task<int> Execute(RequestScenario scenario, MemoryStream responseBody) {
        using var scope = _provider.CreateScope();

        var context = CreateContext(scenario, scope, responseBody);

        await _middleware.GetExecutionChain(context).Next();

        return context.Response.Status ?? 200;
    }

    private static IQueryStringCollection ParseQueryString(string? queryString) {
        if (string.IsNullOrEmpty(queryString)) {
            return EmptyQueryStringCollection.Instance;
        }

        var values = new Dictionary<string, string>();

        foreach (var pair in queryString.Split('&')) {
            var separator = pair.IndexOf('=');

            if (separator > -1) {
                values[pair[..separator]] = pair[(separator + 1)..];
            }
            else {
                values[pair] = "";
            }
        }

        return new SimpleQueryStringCollection(values);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Hardened behind ASP.NET's adapter: the same middleware chain, entered through
/// <c>UseHardened</c> with an <c>HttpContext</c> rather than a constructed execution context.
///
/// Subtracting <see cref="HardenedNativeHarness"/> from this gives the cost of
/// <c>AspNetCoreRequestHandler</c> and <c>AspNetExecutionContext</c> on their own, because
/// everything below the adapter is the identical chain.
/// </summary>
public sealed class HardenedAspNetHarness : IPipelineHarness {
    private readonly ServiceProvider _provider;
    private readonly RequestDelegate _pipeline;

    public string Name => "hardened-aspnet";

    public HardenedAspNetHarness() {
        _provider = HardenedAppFactory.BuildProvider();
        HardenedAppFactory.RunStartup(_provider);

        // UseHardened appends the web filter to the middleware chain itself, which is why this
        // must not share a provider with the native harness.
        var builder = new ApplicationBuilder(_provider);
        builder.UseHardened();

        _pipeline = builder.Build();
    }

    public IServiceProvider Provider => _provider;

    public async Task<int> Execute(RequestScenario scenario, MemoryStream responseBody) {
        using var scope = _provider.CreateScope();

        var context = HttpContextFactory.Create(scenario, scope.ServiceProvider, responseBody);

        await _pipeline(context);

        return context.Response.StatusCode;
    }

    public void Dispose() => _provider.Dispose();
}
