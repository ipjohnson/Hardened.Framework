using Hardened.Web.Testing;
using Microsoft.AspNetCore.Builder;

namespace Hardened.Web.AspNetCore.Testing;

/// <summary>
/// The ASP.NET Core pipeline on Kestrel over the test's container, which is the application's
/// own container: <see cref="AspNetCoreTestingAttribute"/> built it and handed the runner
/// <c>app.Services</c>.
/// </summary>
/// <remarks>
/// A singleton in <c>app.Services</c>, so the runner's disposal of the provider reaches it before
/// Kestrel's own registration: the transport is closed, the application is stopped within the
/// bound, and then disposed. Disposing the application disposes the provider that is already
/// being disposed, which is safe only because the container's own dispose is guarded against
/// re-entry.
/// </remarks>
public sealed class AspNetCoreTestHost : SocketHost {
    private WebApplication? _app;

    internal AspNetCoreTestHost(IAspNetCoreTestComposition composition, string environmentName) {
        Composition = composition;
        EnvironmentName = environmentName;
    }

    public IAspNetCoreTestComposition Composition { get; }

    /// <summary>The environment name the application is built under: the Hardened one in scope.</summary>
    public string EnvironmentName { get; }

    /// <summary>The application, once <see cref="AspNetCoreTestingAttribute"/> has built it.</summary>
    public WebApplication Application =>
        _app ?? throw new InvalidOperationException("The application has not been built; [assembly: AspNetCoreTesting] builds it when the runner asks for the container.");

    public override bool IsTerminal => false;

    internal void Attach(WebApplication app) => _app = app;

    protected override async Task<Uri> Listen(IServiceProvider provider, CancellationToken cancellationToken) {
        var app = Application;

        // UseHardened runs the startup services through the guarded ApplicationLogic.Start, so a
        // test whose entry point already ran them does not run them again, and appends the
        // routing and handler filter.
        Composition.Configure(app);

        await app.StartAsync(cancellationToken);

        return new Uri(app.Urls.First());
    }

    protected override async Task StopAsync(CancellationToken bounded) {
        if (_app == null) {
            return;
        }

        await _app.StopAsync(bounded);
        await _app.DisposeAsync();
    }
}
