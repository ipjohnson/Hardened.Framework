using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests;

/// <summary>
/// <c>UseHardened</c>, which is the one line an ASP.NET-hosted application writes.
/// </summary>
/// <remarks>
/// <para>
/// CI measured it at <b>0%</b>. Eight lines, in the public entry point of the package, and every
/// ASP.NET consumer runs them — the suite reached the handler underneath but never the call that
/// installs it.
/// </para>
/// <para>
/// It does two separate things that both have to happen: it inserts the middleware that hands a
/// request to Hardened, and it registers the web handler into the middleware chain Hardened runs
/// internally. Doing only the first produces an application that accepts requests and routes none
/// of them.
/// </para>
/// </remarks>
public class AspNetCoreExtensionsTests {

    private static IApplicationBuilder Builder(
        IMiddlewareService? middleware = null,
        IWebExecutionHandlerService? handler = null,
        params IStartupService[] startupServices) {
        var services = new ServiceCollection();

        if (middleware != null) {
            services.AddSingleton(middleware);
        }

        if (handler != null) {
            services.AddSingleton(handler);
        }

        foreach (var startupService in startupServices) {
            services.AddSingleton(startupService);
        }

        return new ApplicationBuilder(services.BuildServiceProvider());
    }

    /// <summary>A startup service that records that it ran, and what it appended.</summary>
    private sealed class RecordingStartupService(Action<IServiceProvider> onStartup) : IStartupService {
        public int Runs { get; private set; }

        public Task<bool> Startup(IServiceProvider rootProvider) {
            Runs++;
            onStartup(rootProvider);

            return Task.FromResult(true);
        }
    }

    [Fact]
    public void UseHardenedRegistersTheWebHandlerWithTheMiddlewareService() {
        var middleware = Substitute.For<IMiddlewareService>();
        var handler = Substitute.For<IWebExecutionHandlerService>();

        Builder(middleware, handler).UseHardened();

        middleware.Received(1).Use(Arg.Any<Func<IExecutionContext, IExecutionFilter>>());
    }

    /// <summary>
    /// The handler it registers is the one resolved from the container, not a fresh one — the
    /// service holds the routing table.
    /// </summary>
    [Fact]
    public void TheRegisteredFilterIsTheResolvedWebHandler() {
        var middleware = Substitute.For<IMiddlewareService>();
        var handler = Substitute.For<IWebExecutionHandlerService>();

        Func<IExecutionContext, IExecutionFilter>? registered = null;

        middleware.Use(Arg.Do<Func<IExecutionContext, IExecutionFilter>>(value => registered = value));

        Builder(middleware, handler).UseHardened();

        Assert.NotNull(registered);
        Assert.Same(handler, registered!(Substitute.For<IExecutionContext>()));
    }

    /// <summary>
    /// Returned so it chains, which is how every ASP.NET pipeline is written.
    /// </summary>
    [Fact]
    public void UseHardenedReturnsTheBuilder() {
        var builder = Builder(Substitute.For<IMiddlewareService>(), Substitute.For<IWebExecutionHandlerService>());

        Assert.Same(builder, builder.UseHardened());
    }

    /// <summary>
    /// A missing registration fails at startup rather than on the first request. An application
    /// that called <c>UseHardened</c> without the module registered would otherwise accept traffic
    /// and refuse all of it.
    /// </summary>
    [Fact]
    public void UseHardenedThrowsWhenTheMiddlewareServiceIsMissing() {
        Assert.Throws<InvalidOperationException>(
            () => Builder(handler: Substitute.For<IWebExecutionHandlerService>()).UseHardened());
    }

    [Fact]
    public void UseHardenedThrowsWhenTheWebHandlerServiceIsMissing() {
        Assert.Throws<InvalidOperationException>(
            () => Builder(Substitute.For<IMiddlewareService>()).UseHardened());
    }

    #region startup services

    /// <summary>
    /// The third thing <c>UseHardened</c> has to do. An <c>IStartupService</c> is where the
    /// framework's own authentication and CORS filters come from, so a host that runs none of them
    /// serves every request with no principal and refuses the authorized ones.
    /// </summary>
    [Fact]
    public void UseHardenedRunsTheRegisteredStartupServices() {
        var startup = new RecordingStartupService(_ => { });

        Builder(Substitute.For<IMiddlewareService>(), Substitute.For<IWebExecutionHandlerService>(), startup)
            .UseHardened();

        Assert.Equal(1, startup.Runs);
    }

    [Fact]
    public void EachStartupServiceReceivesTheApplicationServices() {
        IServiceProvider? received = null;
        var startup = new RecordingStartupService(provider => received = provider);

        var builder = Builder(
            Substitute.For<IMiddlewareService>(), Substitute.For<IWebExecutionHandlerService>(), startup);

        builder.UseHardened();

        Assert.Same(builder.ApplicationServices, received);
    }

    /// <summary>
    /// The ordering the Kestrel host documents, asserted here because the ASP.NET bridge had it
    /// backwards. The web handler is terminal - it does not call <c>Next()</c> - so a filter a
    /// startup service appends after it never runs.
    /// </summary>
    [Fact]
    public void TheWebHandlerIsRegisteredAfterTheStartupServicesHaveRun() {
        var middleware = Substitute.For<IMiddlewareService>();
        var handler = Substitute.For<IWebExecutionHandlerService>();
        var order = new List<string>();

        middleware.Use(Arg.Do<Func<IExecutionContext, IExecutionFilter>>(
            value => order.Add(ReferenceEquals(value(Substitute.For<IExecutionContext>()), handler)
                ? "handler"
                : "startup")));

        var startup = new RecordingStartupService(provider =>
            provider.GetRequiredService<IMiddlewareService>().Use(_ => Substitute.For<IExecutionFilter>()));

        Builder(middleware, handler, startup).UseHardened();

        Assert.Equal(["startup", "handler"], order);
    }

    /// <summary>
    /// An application scaffolded before <c>UseHardened</c> ran startup itself still calls
    /// <c>ApplicationLogic.Start</c> afterwards. Running the set twice would install the
    /// authentication middleware and the CORS filter twice over.
    /// </summary>
    [Fact]
    public async Task StartAfterUseHardenedRunsTheStartupServicesNoSecondTime() {
        var startup = new RecordingStartupService(_ => { });

        var builder = Builder(
            Substitute.For<IMiddlewareService>(), Substitute.For<IWebExecutionHandlerService>(), startup);

        builder.UseHardened();

        await ApplicationLogic.Start(builder.ApplicationServices, null);

        Assert.Equal(1, startup.Runs);
    }

    #endregion

    #region the middleware itself

    [Fact]
    public async Task TheMiddlewareHandsTheRequestToTheResolvedHandler() {
        var handler = Substitute.For<IAspNetCoreRequestHandler>();

        handler.HandleRequest(Arg.Any<HttpContext>(), Arg.Any<RequestDelegate>())
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();

        services.AddSingleton(handler);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        await AspNetCoreExtensions.HardenedMiddleware(httpContext, _ => Task.CompletedTask);

        await handler.Received(1).HandleRequest(httpContext, Arg.Any<RequestDelegate>());
    }

    /// <summary>
    /// Resolved from <c>RequestServices</c> rather than captured at startup, so a handler
    /// registered scoped gets the request's scope.
    /// </summary>
    [Fact]
    public async Task TheHandlerIsResolvedPerRequest() {
        var services = new ServiceCollection();

        services.AddScoped<IAspNetCoreRequestHandler>(_ => {
            var handler = Substitute.For<IAspNetCoreRequestHandler>();

            handler.HandleRequest(Arg.Any<HttpContext>(), Arg.Any<RequestDelegate>())
                .Returns(Task.CompletedTask);

            return handler;
        });

        var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IAspNetCoreRequestHandler>(),
            second.ServiceProvider.GetRequiredService<IAspNetCoreRequestHandler>());

        await AspNetCoreExtensions.HardenedMiddleware(
            new DefaultHttpContext { RequestServices = first.ServiceProvider }, _ => Task.CompletedTask);
    }

    [Fact]
    public async Task TheNextDelegateIsPassedThroughSoTheHandlerCanFallThrough() {
        var handler = Substitute.For<IAspNetCoreRequestHandler>();
        RequestDelegate? seen = null;

        handler.HandleRequest(Arg.Any<HttpContext>(), Arg.Do<RequestDelegate>(value => seen = value))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();

        services.AddSingleton(handler);

        RequestDelegate next = _ => Task.CompletedTask;

        await AspNetCoreExtensions.HardenedMiddleware(
            new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }, next);

        Assert.Same(next, seen);
    }

    #endregion
}
