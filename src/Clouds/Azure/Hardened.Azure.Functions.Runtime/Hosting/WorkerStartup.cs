using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Hosting;

namespace Hardened.Azure.Functions.Runtime.Hosting;

/// <summary>
/// Runs the application's startup services when the worker's host starts, before the worker
/// connects to the Functions host.
/// </summary>
/// <remarks>
/// <para>
/// Every host calls <c>ApplicationLogic.Start</c> before it takes a request: the startup services
/// install authentication, the authorization filter, CORS, the routes registered at startup and
/// the global filters. Without it a deployed function app serves every route with none of them,
/// and no test notices, because the test hosts run the startup services themselves.
/// </para>
/// <para>
/// <c>StartingAsync</c> rather than <c>StartAsync</c>, because of order.
/// <c>ConfigureFunctionsWorkerDefaults</c> registers the worker's own hosted service, which opens
/// the channel invocations arrive on, before it calls <c>UseHardened</c>, and hosted services start
/// in the order they were registered. The generic host calls every <c>StartingAsync</c> before the
/// first <c>StartAsync</c>.
/// </para>
/// </remarks>
internal sealed class WorkerStartup(IServiceProvider provider) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) =>
        ApplicationLogic.Start(provider, null);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
