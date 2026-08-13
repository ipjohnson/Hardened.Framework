using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Runtime;

/// <summary>
/// A Hardened application listening on Kestrel, without the ASP.NET Core request pipeline.
///
/// <code>
/// var services = new ServiceCollection();
/// services.AddLogging();
/// services.AddTransient&lt;IHardenedEnvironment&gt;(_ =&gt; new EnvironmentImpl(arguments: args));
/// new Application().PopulateServiceCollection(services);
///
/// await using var app = HardenedKestrelApplication.Create(
///     services, kestrel =&gt; kestrel.ListenAnyIP(5000));
///
/// await app.RunAsync();
/// </code>
///
/// The caller populates the service collection rather than passing a module, because
/// <c>PopulateServiceCollection</c> is generated onto each module class rather than declared on
/// an interface — there is no way to reach it generically.
///
/// This owns the service provider it builds. To run inside a generic host instead, so that
/// configuration, logging and shutdown come from the host, use
/// <c>services.AddHardenedKestrel(...)</c>.
/// </summary>
public sealed class HardenedKestrelApplication : IAsyncDisposable {
    private readonly ServiceProvider _provider;
    private readonly KestrelServerRunner _runner;

    private HardenedKestrelApplication(ServiceProvider provider, KestrelServerRunner runner) {
        _provider = provider;
        _runner = runner;
    }

    public IServiceProvider Services => _provider;

    /// <summary>The addresses Kestrel bound to. Populated once the application has started.</summary>
    public IReadOnlyCollection<string> Addresses => _runner.Addresses;

    public static HardenedKestrelApplication Create(
        IServiceCollection services,
        Action<KestrelServerOptions>? configureKestrel = null,
        Action<SocketTransportOptions>? configureTransport = null) {
        var provider = services.BuildServiceProvider();

        return new HardenedKestrelApplication(
            provider, new KestrelServerRunner(provider, configureKestrel, configureTransport));
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _runner.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _runner.StopAsync(cancellationToken);

    public bool IsStarted => _runner.IsStarted;

    /// <summary>
    /// Starts listening if it has not already, and returns when the token is cancelled, or on
    /// Ctrl-C or SIGTERM. In-flight requests are drained before this returns.
    ///
    /// Starting is conditional so that a caller who needs something in between — reading
    /// <see cref="Addresses"/> after binding, most often — can call <see cref="StartAsync"/>
    /// first and then hand over to this.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default) {
        if (!IsStarted) {
            await StartAsync(cancellationToken);
        }

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = cancellationToken.Register(() => shutdown.TrySetResult());

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs) {
            // Take over the signal so the process drains rather than terminating immediately.
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        }

        void OnProcessExit(object? sender, EventArgs eventArgs) => shutdown.TrySetResult();

        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try {
            await shutdown.Task;
        }
        finally {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            await StopAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync() {
        await _runner.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
