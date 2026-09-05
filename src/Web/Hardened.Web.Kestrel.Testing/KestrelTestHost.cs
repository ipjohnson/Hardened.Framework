using System.Net;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Testing;

namespace Hardened.Web.Kestrel.Testing;

/// <summary>
/// Kestrel over the test's container, through the same <see cref="KestrelServerRunner"/> the
/// standalone and hosted Kestrel applications run through.
/// </summary>
/// <remarks>
/// The runner runs the startup services through the guarded <c>ApplicationLogic.Start</c>, so
/// they run once however many attributes reached them, appends the routing and handler filter,
/// and binds. Port 0: the kernel hands out a port no socket holds, and the address is read back
/// from the server after it started rather than computed, so two tests, two classes or two
/// processes binding at once cannot collide.
/// </remarks>
internal sealed class KestrelTestHost : SocketHost {
    private KestrelServerRunner? _runner;

    public override bool IsTerminal => true;

    protected override async Task<Uri> Listen(IServiceProvider provider, CancellationToken cancellationToken) {
        _runner = new KestrelServerRunner(provider, kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        await _runner.StartAsync(cancellationToken);

        return new Uri(_runner.Addresses.Single());
    }

    /// <remarks>
    /// Stopped here with the bound rather than left to <see cref="KestrelServerRunner.DisposeAsync"/>,
    /// which stops with <c>CancellationToken.None</c> and would wait on a hung handler for ever.
    /// Kestrel closes what is idle at once and aborts what is still running when the bound fires.
    /// </remarks>
    protected override async Task StopAsync(CancellationToken bounded) {
        if (_runner == null) {
            return;
        }

        await _runner.StopAsync(bounded);
        await _runner.DisposeAsync();
    }
}
