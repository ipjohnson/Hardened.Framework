using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// Constructs Kestrel and drives its lifecycle against a Hardened application.
///
/// Shared by the two hosting modes so that startup ordering exists in one place: the standalone
/// <see cref="HardenedKestrelApplication"/>, which owns its service provider, and
/// <c>HardenedKestrelHostedService</c>, which runs inside a generic host and uses the host's.
/// </summary>
public sealed class KestrelServerRunner : IAsyncDisposable {
    private readonly IServiceProvider _provider;
    private readonly IServer _server;
    private readonly IHttpApplication<HardenedHttpApplication.RequestContext> _application;
    private bool _started;
    private bool _stopped;

    public KestrelServerRunner(
        IServiceProvider provider,
        Action<KestrelServerOptions>? configureKestrel = null,
        Action<SocketTransportOptions>? configureTransport = null) {
        _provider = provider;

        // Resolved by interface rather than concrete type: [SingletonService] registers a class
        // against the interfaces it implements, and depending on the abstraction also leaves room
        // for an application to substitute its own IHttpApplication.
        _application =
            provider.GetRequiredService<IHttpApplication<HardenedHttpApplication.RequestContext>>();

        var loggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        var kestrelOptions = new KestrelServerOptions {
            // Kestrel resolves its own internals through this — the HTTPS configuration and the
            // connection middleware both read it. Left unset, binding an HTTPS endpoint throws.
            ApplicationServices = provider
        };

        if (configureKestrel is null) {
            kestrelOptions.ListenAnyIP(DefaultPort);
        }
        else {
            configureKestrel(kestrelOptions);
        }

        var transportOptions = new SocketTransportOptions();
        configureTransport?.Invoke(transportOptions);

        _server = new KestrelServer(
            Options.Create(kestrelOptions),
            new SocketTransportFactory(Options.Create(transportOptions), loggerFactory),
            loggerFactory);
    }

    public const int DefaultPort = 5000;

    public bool IsStarted => _started;

    /// <summary>The addresses Kestrel bound to. Populated once <see cref="StartAsync"/> has run.</summary>
    public IReadOnlyCollection<string> Addresses =>
        _server.Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? Array.Empty<string>();

    /// <summary>
    /// Runs the registered startup services, attaches the routing filter, then begins listening.
    ///
    /// The order matters. Startup services populate the filter registry and CORS configuration,
    /// so a server that begins listening first can serve a request against a half-built filter
    /// set.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        if (_started) {
            throw new InvalidOperationException("The server has already been started.");
        }

        _started = true;

        // Through the guard, so the services run once per provider however the host was reached:
        // a Program.cs that already called ApplicationLogic.Start, or a test whose entry point
        // attribute did. A loop of this class's own ran them a second time, and a startup service
        // that appends a filter appended it twice.
        await ApplicationLogic.Start(_provider, null);

        // The equivalent of what UseHardened does for the ASP.NET pipeline: put the routing and
        // handler filter at the end of the middleware chain. MiddlewareService is a singleton
        // holding a plain list, so this must happen exactly once per provider — which is why it
        // lives here rather than in a constructor that could plausibly be called twice.
        var handler = _provider.GetRequiredService<IWebExecutionHandlerService>();
        _provider.GetRequiredService<IMiddlewareService>().Use(_ => handler);

        await _server.StartAsync(_application, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default) {
        if (!_started || _stopped) {
            return;
        }

        _stopped = true;

        await _server.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() {
        await StopAsync(CancellationToken.None);

        if (_server is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync();
        }
        else {
            (_server as IDisposable)?.Dispose();
        }
    }
}
