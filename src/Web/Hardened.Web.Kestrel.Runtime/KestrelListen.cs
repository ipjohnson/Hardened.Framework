using System.Globalization;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Hardened.Web.Kestrel.Runtime;

/// <summary>
/// Binding the port a container platform names in the environment.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run and Container Apps both inject <c>PORT</c> and expect the container to listen on it,
/// on every interface, and both default it to 8080. Reading it is not a cloud concern, which is why
/// this lives beside the host rather than in a cloud package: the same line serves a Dockerfile
/// deployed anywhere.
/// </para>
/// <para>
/// Opt-in. <see cref="HardenedKestrelApplication.Create"/> keeps its default of port 5000 on every
/// interface, so nothing that exists changes behaviour; a host that wants the platform's port says
/// <c>kestrel =&gt; KestrelListen.FromEnvironment(kestrel)</c>.
/// </para>
/// </remarks>
public static class KestrelListen {
    public const string PortVariable = "PORT";

    public const int DefaultPort = 8080;

    /// <summary>
    /// Every interface on the port <c>PORT</c> names, or <paramref name="defaultPort"/> when the
    /// variable is unset or is not a port.
    /// </summary>
    public static void FromEnvironment(KestrelServerOptions kestrel, int defaultPort = DefaultPort) =>
        kestrel.ListenAnyIP(Port(Environment.GetEnvironmentVariable(PortVariable), defaultPort));

    /// <summary>
    /// The port a value names, or <paramref name="defaultPort"/> when it does not name one.
    /// </summary>
    /// <remarks>
    /// Falling back rather than failing, deliberately: the variable is the platform's to set, and a
    /// container started by hand without it should come up on the documented default rather than
    /// refuse to start over a setting the platform would have supplied.
    /// </remarks>
    public static int Port(string? configured, int defaultPort = DefaultPort) =>
        int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
        port is > 0 and <= 65535
            ? port
            : defaultPort;
}
