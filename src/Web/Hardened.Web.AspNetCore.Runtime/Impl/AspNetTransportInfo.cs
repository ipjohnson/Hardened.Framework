using System.Globalization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

/// <summary>
/// The connection, as ASP.NET Core describes it.
/// </summary>
/// <remarks>
/// <para>
/// Read on demand, for the reason <c>FeatureTransportInfo</c> is: most requests ask for none of it.
/// </para>
/// <para>
/// <b><c>client.address</c> is <c>Connection.RemoteIpAddress</c>, and on this host that may already
/// be forwarded-corrected.</b> ASP.NET's own <c>ForwardedHeadersMiddleware</c> rewrites
/// <c>RemoteIpAddress</c> in place when an application enables it, so what is reported here is
/// whatever ran before <c>UseHardened()</c>. That is the right answer either way - it is what the
/// host believes the client to be - and it is why the peer is published separately, where it is
/// not rewritten.
/// </para>
/// </remarks>
public sealed class AspNetTransportInfo : ITransportInfo {
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

    private readonly HttpRequest _request;

    public AspNetTransportInfo(HttpRequest request) {
        _request = request;
    }

    public IReadOnlyList<string> Keys => KeyList;

    public string? Get(string key) {
        var connection = _request.HttpContext.Connection;

        return key switch {
            KnownTransportKeys.ClientAddress or KnownTransportKeys.NetworkPeerAddress =>
                connection.RemoteIpAddress?.ToString(),

            KnownTransportKeys.ClientPort or KnownTransportKeys.NetworkPeerPort =>
                Port(connection.RemotePort),

            KnownTransportKeys.ServerAddress => connection.LocalIpAddress?.ToString(),
            KnownTransportKeys.ServerPort => Port(connection.LocalPort),

            // ASP.NET reports "HTTP/1.1"; the convention wants the version alone.
            KnownTransportKeys.NetworkProtocolVersion => Version(_request.Protocol),

            KnownTransportKeys.UrlScheme => _request.Scheme,

            _ => null
        };
    }

    /// <summary>A port, or null when there is none - 0 is an absence, not a port.</summary>
    private static string? Port(int port) =>
        port == 0 ? null : port.ToString(CultureInfo.InvariantCulture);

    private static string? Version(string? protocol) {
        if (string.IsNullOrEmpty(protocol)) {
            return null;
        }

        var slash = protocol!.IndexOf('/');

        return slash > -1 ? protocol.Substring(slash + 1) : protocol;
    }
}
