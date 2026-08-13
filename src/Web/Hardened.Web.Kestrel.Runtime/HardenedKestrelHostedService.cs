using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hardened.Web.Kestrel.Runtime;

/// <summary>
/// Options carrying the Kestrel configuration through DI to <see cref="HardenedKestrelHostedService"/>.
/// </summary>
public sealed class HardenedKestrelOptions {
    public Action<KestrelServerOptions>? ConfigureKestrel { get; set; }

    public Action<SocketTransportOptions>? ConfigureTransport { get; set; }
}

/// <summary>
/// Runs Hardened on Kestrel inside a generic host.
///
/// This is the middle path between the two extremes. <c>ConfigureWebHostDefaults</c> would bring
/// back <c>GenericWebHostService</c>, <c>HostingApplication</c> and the whole ASP.NET pipeline —
/// the things this package exists to avoid. Running with no host at all gives the fastest start
/// but gives up configuration binding, logging setup, and coordinated shutdown. Registering here
/// keeps <c>Microsoft.Extensions.Hosting</c> and drops only <c>Microsoft.AspNetCore.Hosting</c>.
/// </summary>
public sealed class HardenedKestrelHostedService : IHostedService, IAsyncDisposable {
    private readonly KestrelServerRunner _runner;

    public HardenedKestrelHostedService(
        IServiceProvider provider, HardenedKestrelOptions options) {
        _runner = new KestrelServerRunner(
            provider, options.ConfigureKestrel, options.ConfigureTransport);
    }

    public IReadOnlyCollection<string> Addresses => _runner.Addresses;

    public Task StartAsync(CancellationToken cancellationToken) =>
        _runner.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _runner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _runner.DisposeAsync();
}

public static class HardenedKestrelServiceCollectionExtensions {

    /// <summary>
    /// Registers Hardened on Kestrel as a hosted service.
    ///
    /// The caller is still responsible for populating the collection with the Hardened module —
    /// this only adds the server on top of it.
    /// </summary>
    public static IServiceCollection AddHardenedKestrel(
        this IServiceCollection services,
        Action<KestrelServerOptions>? configureKestrel = null,
        Action<SocketTransportOptions>? configureTransport = null) {
        services.AddSingleton(new HardenedKestrelOptions {
            ConfigureKestrel = configureKestrel,
            ConfigureTransport = configureTransport
        });

        services.AddSingleton<HardenedKestrelHostedService>();
        services.AddSingleton<IHostedService>(
            provider => provider.GetRequiredService<HardenedKestrelHostedService>());

        return services;
    }
}
