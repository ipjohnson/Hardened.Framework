using System.Globalization;
using System.Runtime.InteropServices;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Hardened.Gcp.CloudRun.Runtime.Hosting;

/// <summary>
/// The Cloud Run container contract, for a <c>Program.cs</c>: listen on <c>PORT</c>, drain on
/// <c>SIGTERM</c>.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run sends requests to the port named by <c>PORT</c>, 8080 when it is unset, and the
/// container has to listen on <c>0.0.0.0</c>. On shutdown it sends <c>SIGTERM</c> and, ten seconds
/// later, <c>SIGKILL</c>; a request still in flight when the signal arrives has those ten seconds
/// to finish. <see cref="Listen"/> is the first half and <see cref="RunAsync"/> the second.
/// </para>
/// <para>
/// <c>HardenedKestrelApplication.RunAsync</c> returns on <c>ProcessExit</c>, which is what the
/// runtime raises for <c>SIGTERM</c> when nothing has registered for the signal - and then exits as
/// soon as the handlers return, before the server has stopped. Registering the signal with
/// <c>Cancel = true</c> takes it away from the runtime, so the stop that follows is a drain rather
/// than a race with process exit. The same registration the generic host's console lifetime makes.
/// </para>
/// </remarks>
public static class CloudRunHost {
    /// <summary>The variable Cloud Run names the port in.</summary>
    public const string PortVariable = "PORT";

    /// <summary>What Cloud Run sends to when <see cref="PortVariable"/> is unset.</summary>
    public const int DefaultPort = 8080;

    /// <summary>
    /// How long a stop is given before what is left is aborted: Cloud Run's own grace period, so a
    /// drain that would outlive the container ends on a Kestrel abort rather than a kill.
    /// </summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);

    /// <summary>The port Cloud Run asked for, or <see cref="DefaultPort"/>.</summary>
    public static int Port() => Port(Environment.GetEnvironmentVariable(PortVariable));

    /// <summary>
    /// <paramref name="configured"/> as a port, or <see cref="DefaultPort"/> when it is unset or
    /// not one.
    /// </summary>
    public static int Port(string? configured) =>
        int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
        port is > 0 and <= 65535
            ? port
            : DefaultPort;

    /// <summary>
    /// Binds every address on the port Cloud Run asked for.
    /// </summary>
    /// <code>
    /// await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);
    /// </code>
    public static void Listen(KestrelServerOptions kestrel) => kestrel.ListenAnyIP(Port());

    /// <summary>
    /// Starts the application if it is not already started, waits for <c>SIGTERM</c>, <c>SIGINT</c>
    /// or <paramref name="cancellationToken"/>, then stops it, letting what is in flight finish
    /// within <see cref="ShutdownGrace"/>.
    /// </summary>
    public static async Task RunAsync(HardenedKestrelApplication app, CancellationToken cancellationToken = default) {
        if (!app.IsStarted) {
            await app.StartAsync(cancellationToken);
        }

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSignal(PosixSignalContext context) {
            // Taken over, so the runtime neither exits on the signal nor raises ProcessExit for it;
            // the process ends when this method returns and Main completes.
            context.Cancel = true;
            shutdown.TrySetResult();
        }

        using var registration = cancellationToken.Register(() => shutdown.TrySetResult());
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);

        await shutdown.Task;

        using var grace = new CancellationTokenSource(ShutdownGrace);

        await app.StopAsync(grace.Token);
    }
}
