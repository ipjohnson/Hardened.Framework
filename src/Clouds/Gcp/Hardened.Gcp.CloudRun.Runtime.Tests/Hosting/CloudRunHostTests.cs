using System.Net;
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Hosting;

/// <summary>
/// The container contract's two halves: the port, and a stop that returns.
/// </summary>
/// <remarks>
/// The signal itself is proven in the container tier, where a real <c>SIGTERM</c> reaches a real
/// process with a request in flight; here the token stands in for it.
/// </remarks>
public class CloudRunHostTests {

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("9090", 9090)]
    [InlineData("not a port", 8080)]
    [InlineData("0", 8080)]
    [InlineData("70000", 8080)]
    [InlineData("-1", 8080)]
    public void ThePortIsWhatCloudRunSaidOrEightyEighty(string? configured, int expected) {
        Assert.Equal(expected, CloudRunHost.Port(configured));
    }

    [Fact]
    public void TheEnvironmentIsReadForThePort() {
        Environment.SetEnvironmentVariable(CloudRunHost.PortVariable, "9191");

        try {
            Assert.Equal(9191, CloudRunHost.Port());
        }
        finally {
            Environment.SetEnvironmentVariable(CloudRunHost.PortVariable, null);
        }
    }

    [Fact]
    public async Task RunAsyncStartsTheApplicationAndReturnsWhenTheTokenIsCancelled() {
        var startup = Substitute.For<IStartupService>();
        startup.Startup(Arg.Any<IServiceProvider>()).Returns(true);

        var services = new ServiceCollection();

        services.AddSingleton(startup);
        services.AddSingleton(Substitute.For<IMiddlewareService>());
        services.AddSingleton(Substitute.For<IWebExecutionHandlerService>());
        services.AddSingleton(Substitute.For<IHttpApplication<HardenedHttpApplication.RequestContext>>());

        await using var app = HardenedKestrelApplication.Create(
            services, kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        using var shutdown = new CancellationTokenSource();

        var running = CloudRunHost.RunAsync(app, shutdown.Token);

        // Started by RunAsync itself, and still running until told otherwise.
        Assert.True(app.IsStarted);
        Assert.False(running.IsCompleted);

        await shutdown.CancelAsync();

        await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await startup.Received(1).Startup(Arg.Any<IServiceProvider>());
    }
}
