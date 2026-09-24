using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Hardened.IntegrationTests.AzureHttp.SUT.Tests;

/// <summary>
/// What the worker's own start runs.
/// </summary>
/// <remarks>
/// <para>
/// Every other test here runs under <c>[AzureFunctionsWebTesting]</c>, which runs the startup
/// services itself, so none of them can see whether a deployed worker does. This one builds the
/// host the way <c>Program.cs</c> does, with <c>ConfigureFunctionsWorkerDefaults</c> and
/// <c>UseHardened</c>, and starts it.
/// </para>
/// <para>
/// One registration is replaced: the worker's own hosted service, which opens the channel to the
/// Functions host that invocations arrive on. A stand-in takes its place in the registration order
/// and records what had run when it started, which is the first moment an invocation could arrive.
/// </para>
/// </remarks>
public class WorkerStartupTests
{
    [Fact]
    public async Task TheStartupServicesRunOnceBeforeTheWorkerConnects()
    {
        var probe = new StartupProbe();
        var worker = new WorkerStandIn(probe);

        using var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults(builder => builder.UseHardened<AzureHttpTestApp>())
            .ConfigureServices(services =>
            {
                services.AddSingleton<IStartupService>(probe);

                ReplaceTheWorker(services, worker);
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal(1, worker.StartupRunsWhenStarted);
            Assert.Equal(1, probe.Runs);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Puts <paramref name="standIn"/> where <c>ConfigureFunctionsWorkerDefaults</c> registered the
    /// worker's hosted service. That type is internal to the worker, so it is found by name.
    /// </summary>
    private static void ReplaceTheWorker(IServiceCollection services, IHostedService standIn)
    {
        var worker = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Name == "WorkerHostedService"
        );

        services[services.IndexOf(worker)] = new ServiceDescriptor(typeof(IHostedService), standIn);
    }

    private sealed class WorkerStandIn(StartupProbe probe) : IHostedService
    {
        public int? StartupRunsWhenStarted { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartupRunsWhenStarted = probe.Runs;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StartupProbe : IStartupService
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public Task<bool> Startup(IServiceProvider rootProvider)
        {
            Interlocked.Increment(ref _runs);

            return Task.FromResult(true);
        }
    }
}
