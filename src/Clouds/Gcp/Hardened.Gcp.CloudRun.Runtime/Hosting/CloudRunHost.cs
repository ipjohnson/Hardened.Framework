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
/// to finish. <see cref="Listen"/> is the first half and <see cref="RunAsync"/> the second, and
/// both are the Kestrel host's own members with Cloud Run's values filled in:
/// <see cref="KestrelListen.FromEnvironment"/>, and the <c>RunAsync</c> overload that takes the
/// signal away from the runtime and stops within a grace period. The plain
/// <c>HardenedKestrelApplication.RunAsync</c> returns on <c>ProcessExit</c> and the process exits
/// before the server has drained; the container tier's shutdown test saw a request cut off that way.
/// </para>
/// </remarks>
public static class CloudRunHost {
    /// <summary>The variable Cloud Run names the port in.</summary>
    public const string PortVariable = KestrelListen.PortVariable;

    /// <summary>What Cloud Run sends to when <see cref="PortVariable"/> is unset.</summary>
    public const int DefaultPort = KestrelListen.DefaultPort;

    /// <summary>
    /// How long a stop is given before what is left is aborted: Cloud Run's own grace period, so a
    /// drain that would outlive the container ends on a Kestrel abort rather than a kill.
    /// </summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);

    /// <summary>What Cloud Run sends, and what a terminal sends, both drained the same way.</summary>
    public static readonly IReadOnlyList<PosixSignal> Signals = [PosixSignal.SIGTERM, PosixSignal.SIGINT];

    /// <summary>The port Cloud Run asked for, or <see cref="DefaultPort"/>.</summary>
    public static int Port() => Port(Environment.GetEnvironmentVariable(PortVariable));

    /// <summary>
    /// <paramref name="configured"/> as a port, or <see cref="DefaultPort"/> when it is unset or
    /// not one.
    /// </summary>
    public static int Port(string? configured) => KestrelListen.Port(configured, DefaultPort);

    /// <summary>
    /// Binds every address on the port Cloud Run asked for.
    /// </summary>
    /// <code>
    /// await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);
    /// </code>
    public static void Listen(KestrelServerOptions kestrel) => KestrelListen.FromEnvironment(kestrel, DefaultPort);

    /// <summary>
    /// Starts the application if it is not already started, waits for <c>SIGTERM</c>, <c>SIGINT</c>
    /// or <paramref name="cancellationToken"/>, then stops it, letting what is in flight finish
    /// within <see cref="ShutdownGrace"/>.
    /// </summary>
    public static Task RunAsync(HardenedKestrelApplication app, CancellationToken cancellationToken = default) =>
        app.RunAsync(Signals, ShutdownGrace, cancellationToken);
}
