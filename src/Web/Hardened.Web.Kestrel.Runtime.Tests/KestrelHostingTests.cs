using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// Starting and stopping the server.
///
/// The ordering here is the part worth pinning down. Startup services populate the filter registry
/// and the CORS configuration, and the routing filter is attached to a singleton middleware chain,
/// so a server that begins listening before either has happened serves its first requests against
/// a half-built application. None of that produces an error — it produces wrong behaviour on
/// whichever requests arrive first.
///
/// Where a server is genuinely started it binds port 0, so the OS picks a free port and these
/// cannot collide with each other or with anything already running.
/// </summary>
public class KestrelHostingTests {

    [Fact]
    public void Constructor_ResolvesTheApplicationThroughItsInterface() {
        var harness = new Harness();

        // Resolution is by IHttpApplication<> rather than by the concrete type, because
        // [SingletonService] registers a class against the interfaces it implements. Getting this
        // wrong fails at construction with "no service for type HardenedHttpApplication".
        var runner = harness.CreateRunner();

        Assert.False(runner.IsStarted);
    }

    [Fact]
    public async Task StartAsync_RunsTheRegisteredStartupServices() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        await runner.StartAsync(TestContext.Current.CancellationToken);

        await harness.StartupService.Received(1).Startup(Arg.Any<IServiceProvider>());
    }

    /// <summary>
    /// The equivalent of what <c>UseHardened</c> does for the ASP.NET pipeline. Without it the
    /// chain has no routing filter and every request falls through to nothing.
    /// </summary>
    [Fact]
    public async Task StartAsync_AttachesTheRoutingFilterToTheMiddlewareChain() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        await runner.StartAsync(TestContext.Current.CancellationToken);

        harness.MiddlewareService.Received(1)
            .Use(Arg.Any<Func<IExecutionContext, IExecutionFilter>>());
    }

    [Fact]
    public async Task StartAsync_BindsAndReportsTheAddressItIsListeningOn() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        Assert.Empty(runner.Addresses);

        await runner.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(runner.IsStarted);
        Assert.NotEmpty(runner.Addresses);
    }

    /// <summary>
    /// <c>MiddlewareService</c> is a singleton holding a plain list, so starting twice would
    /// append the routing filter a second time and run the whole chain twice per request — a
    /// response that still looks correct while doing double the work.
    /// </summary>
    [Fact]
    public async Task StartAsync_RefusesToStartTwice() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        await runner.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StopAsync_DoesNothingWhenTheServerNeverStarted() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        await runner.StopAsync(TestContext.Current.CancellationToken);

        await harness.StartupService.DidNotReceive().Startup(Arg.Any<IServiceProvider>());
    }

    [Fact]
    public async Task StopAsync_StopsAStartedServer() {
        var harness = new Harness();
        await using var runner = harness.CreateRunner();

        await runner.StartAsync(TestContext.Current.CancellationToken);
        await runner.StopAsync(TestContext.Current.CancellationToken);

        // Stopping twice is a no-op rather than an error, so a host that stops on shutdown and
        // again on dispose does not throw on the way out.
        await runner.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddHardenedKestrel_RegistersTheServerAsAHostedService() {
        var services = new ServiceCollection();

        services.AddHardenedKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Single(services, d => d.ServiceType == typeof(HardenedKestrelOptions));
    }

    [Fact]
    public void AddHardenedKestrel_CarriesTheConfigurationThrough() {
        var services = new ServiceCollection();

        services.AddHardenedKestrel(
            kestrel => kestrel.Listen(IPAddress.Loopback, 0),
            transport => transport.Backlog = 128);

        var options = services.BuildServiceProvider().GetRequiredService<HardenedKestrelOptions>();

        Assert.NotNull(options.ConfigureKestrel);
        Assert.NotNull(options.ConfigureTransport);
    }

    [Fact]
    public async Task Application_StartsStopsAndReportsItsAddress() {
        var harness = new Harness();
        await using var app = HardenedKestrelApplication.Create(
            harness.CreateServices(), kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        Assert.False(app.IsStarted);

        await app.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(app.IsStarted);
        Assert.NotEmpty(app.Addresses);
        Assert.NotNull(app.Services.GetService<IMiddlewareService>());

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <c>RunAsync</c> starts only when it has not already been started, so a caller that needs
    /// something in between — reading the bound address, most often — can start first and then
    /// hand over.
    /// </summary>
    [Fact]
    public async Task Application_RunAsyncReturnsWhenItsTokenIsCancelledAndDoesNotRestart() {
        var harness = new Harness();
        await using var app = HardenedKestrelApplication.Create(
            harness.CreateServices(), kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        // Would throw "already been started" if RunAsync started unconditionally.
        await app.RunAsync(shutdown.Token);

        await harness.StartupService.Received(1).Startup(Arg.Any<IServiceProvider>());
    }

    [Fact]
    public async Task HostedService_StartsAndStopsTheServer() {
        var harness = new Harness();
        await using var hosted = new HardenedKestrelHostedService(
            harness.Provider,
            new HardenedKestrelOptions {
                ConfigureKestrel = kestrel => kestrel.Listen(IPAddress.Loopback, 0)
            });

        await hosted.StartAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(hosted.Addresses);

        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    private class Harness {
        public Harness() {
            StartupService = Substitute.For<IStartupService>();
            StartupService.Startup(Arg.Any<IServiceProvider>()).Returns(true);

            MiddlewareService = Substitute.For<IMiddlewareService>();

            Provider = CreateServices().BuildServiceProvider();
        }

        /// <summary>
        /// The registrations a Hardened module would supply, standing in for one so these exercise
        /// the hosting code rather than the generator.
        /// </summary>
        public IServiceCollection CreateServices() {
            var services = new ServiceCollection();

            services.AddSingleton(StartupService);
            services.AddSingleton(MiddlewareService);
            services.AddSingleton(Substitute.For<IWebExecutionHandlerService>());
            services.AddSingleton(Substitute.For<IHttpApplication<HardenedHttpApplication.RequestContext>>());

            return services;
        }

        public IStartupService StartupService { get; }

        public IMiddlewareService MiddlewareService { get; }

        public ServiceProvider Provider { get; }

        /// <summary>
        /// Port 0 so the OS assigns a free port and these cannot collide. Bound through
        /// Listen rather than ListenLocalhost, which rejects dynamic ports outright:
        /// "Dynamic port binding is not supported when binding to localhost."
        /// </summary>
        public KestrelServerRunner CreateRunner() =>
            new(Provider, kestrel => kestrel.Listen(IPAddress.Loopback, 0));
    }
}
