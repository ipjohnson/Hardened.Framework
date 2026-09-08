using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// The two opt-ins a container platform needs: the port from the environment, and a run that
/// drains on a signal.
/// </summary>
/// <remarks>
/// The signal itself is not sent here. Registering SIGTERM in the test process and raising it would
/// be a test of the runtime's signal plumbing under a runner that also owns the process; what this
/// holds is the contract around it, and the container tier under <c>src/Clouds/Gcp</c> is where a
/// real <c>docker stop</c> shows the drain: a three-second request in flight finishes with a 200.
/// </remarks>
public class ShutdownTests {

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("9090", 9090)]
    [InlineData("not a port", 8080)]
    [InlineData("0", 8080)]
    [InlineData("70000", 8080)]
    [InlineData(" 9090", 8080)]
    public void PortIsTheVariableWhenItNamesOne(string? configured, int expected) {
        Assert.Equal(expected, KestrelListen.Port(configured));
    }

    [Fact]
    public void PortHonoursTheDefaultItIsGiven() {
        Assert.Equal(5000, KestrelListen.Port(null, 5000));
    }

    /// <summary>
    /// The variable is process-wide, so the port is one the kernel just handed back and the
    /// variable is put back afterwards; no other test reads it.
    /// </summary>
    [Fact]
    public async Task FromEnvironmentListensOnThePortTheVariableNames() {
        var port = FreePort();
        var previous = Environment.GetEnvironmentVariable(KestrelListen.PortVariable);

        Environment.SetEnvironmentVariable(KestrelListen.PortVariable, port.ToString());

        try {
            await using var app = HardenedKestrelApplication.Create(
                new Harness().CreateServices(), kestrel => KestrelListen.FromEnvironment(kestrel));

            await app.StartAsync(TestContext.Current.CancellationToken);

            Assert.Contains(app.Addresses, address => address.EndsWith(":" + port, StringComparison.Ordinal));

            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        finally {
            Environment.SetEnvironmentVariable(KestrelListen.PortVariable, previous);
        }
    }

    /// <summary>
    /// Returns on the token as the plain overload does, starts if it has not been started, and the
    /// server has stopped by the time it returns: the address it bound refuses a connection.
    /// </summary>
    [Fact]
    public async Task RunAsyncWithSignalsReturnsOnTheTokenAndStopsTheServer() {
        var harness = new Harness();

        await using var app = HardenedKestrelApplication.Create(
            harness.CreateServices(), kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        using var shutdown = new CancellationTokenSource();

        var run = app.RunAsync([PosixSignal.SIGTERM], TimeSpan.FromSeconds(5), shutdown.Token);

        // Started by RunAsync itself; the address exists once it has.
        var address = await Bound(app);

        await shutdown.CancelAsync();
        await run;

        await harness.StartupService.Received(1).Startup(Arg.Any<IServiceProvider>());
        Assert.True(await Refuses(address), $"{address} still accepted a connection after RunAsync returned.");
    }

    private static async Task<Uri> Bound(HardenedKestrelApplication app) {
        for (var attempt = 0; attempt < 100; attempt++) {
            if (app.Addresses.Count > 0) {
                return new Uri(app.Addresses.First());
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("RunAsync did not start the server.");
    }

    private static async Task<bool> Refuses(Uri address) {
        using var client = new TcpClient();

        try {
            await client.ConnectAsync(address.Host, address.Port, TestContext.Current.CancellationToken);

            return false;
        }
        catch (SocketException) {
            return true;
        }
    }

    private static int FreePort() {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    /// <summary>The registrations a Hardened module would supply, as <c>KestrelHostingTests</c> stands them in.</summary>
    private sealed class Harness {
        public Harness() {
            StartupService = Substitute.For<IStartupService>();
            StartupService.Startup(Arg.Any<IServiceProvider>()).Returns(true);
        }

        public IStartupService StartupService { get; }

        public IServiceCollection CreateServices() {
            var services = new ServiceCollection();

            services.AddSingleton(StartupService);
            services.AddSingleton(Substitute.For<IMiddlewareService>());
            services.AddSingleton(Substitute.For<IWebExecutionHandlerService>());
            services.AddSingleton(Substitute.For<IHttpApplication<HardenedHttpApplication.RequestContext>>());

            return services;
        }
    }
}
