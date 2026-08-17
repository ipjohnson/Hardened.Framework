using System.Globalization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.AspNetCore.Http.Features;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// The connection, as Kestrel's features describe it.
/// </summary>
/// <remarks>
/// <para>
/// Read on demand rather than into a dictionary at construction. Most requests never ask for any of
/// this, and <c>IHttpConnectionFeature</c> is a feature lookup plus an <c>IPAddress.ToString</c>
/// per answer - work worth doing for the requests that want it and not for the ones that do not.
/// </para>
/// <para>
/// <b><see cref="KnownTransportKeys.ClientAddress"/> is answered with the socket peer, and that is
/// correct only because nothing sits in front.</b> Kestrel here is the server the client reached.
/// Behind a proxy the peer is the proxy, and a forwarded-headers filter is what replaces this
/// answer - which is why the peer is also published separately as
/// <see cref="KnownTransportKeys.NetworkPeerAddress"/>, where it stays true either way.
/// </para>
/// </remarks>
public sealed class FeatureTransportInfo : ITransportInfo {
    private static readonly string[] KeyList = [
        KnownTransportKeys.ClientAddress,
        KnownTransportKeys.ClientPort,
        KnownTransportKeys.NetworkPeerAddress,
        KnownTransportKeys.NetworkPeerPort,
        KnownTransportKeys.ServerAddress,
        KnownTransportKeys.ServerPort,
        KnownTransportKeys.NetworkProtocolVersion,
        KnownTransportKeys.UrlScheme
    ];

    private readonly IHttpConnectionFeature? _connection;
    private readonly IHttpRequestFeature _request;

    public FeatureTransportInfo(IHttpConnectionFeature? connection, IHttpRequestFeature request) {
        _connection = connection;
        _request = request;
    }

    public IReadOnlyList<string> Keys => KeyList;

    public string? Get(string key) =>
        key switch {
            KnownTransportKeys.ClientAddress or KnownTransportKeys.NetworkPeerAddress =>
                _connection?.RemoteIpAddress?.ToString(),

            KnownTransportKeys.ClientPort or KnownTransportKeys.NetworkPeerPort =>
                Port(_connection?.RemotePort),

            KnownTransportKeys.ServerAddress => _connection?.LocalIpAddress?.ToString(),
            KnownTransportKeys.ServerPort => Port(_connection?.LocalPort),

            // Kestrel reports "HTTP/1.1"; the convention wants the version alone.
            KnownTransportKeys.NetworkProtocolVersion => Version(_request.Protocol),

            KnownTransportKeys.UrlScheme => _request.Scheme,

            _ => null
        };

    /// <summary>
    /// A port, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Kestrel reports 0 for a connection with no port - a Unix domain socket, or a named pipe -
    /// and "0" is a port number rather than an absence. Null is the honest answer.
    /// </remarks>
    private static string? Port(int? port) =>
        port is null or 0 ? null : port.Value.ToString(CultureInfo.InvariantCulture);

    private static string? Version(string? protocol) {
        if (string.IsNullOrEmpty(protocol)) {
            return null;
        }

        var slash = protocol!.IndexOf('/');

        return slash > -1 ? protocol.Substring(slash + 1) : protocol;
    }
}
