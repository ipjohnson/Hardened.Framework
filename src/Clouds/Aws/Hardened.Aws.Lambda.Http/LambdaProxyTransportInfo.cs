using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.Http;

/// <summary>
/// The connection, as a payload format 1.0 event describes it.
/// </summary>
/// <remarks>
/// The same keys as <see cref="LambdaHttpTransportInfo"/>, for the same reasons. The network peer
/// is absent because the function is never told the front door's own address.
/// </remarks>
public sealed class LambdaProxyTransportInfo : ITransportInfo
{
    private static readonly string[] KeyList =
    [
        KnownTransportKeys.ClientAddress,
        KnownTransportKeys.ServerAddress,
        KnownTransportKeys.NetworkProtocolVersion,
        KnownTransportKeys.UrlScheme,
    ];

    private readonly string? _clientAddress;
    private readonly string? _serverAddress;
    private readonly string? _protocol;
    private readonly string _scheme;

    public LambdaProxyTransportInfo(
        string? clientAddress,
        string? serverAddress,
        string? protocol,
        string? scheme = null
    )
    {
        _clientAddress = Empty(clientAddress);
        _serverAddress = Empty(serverAddress);
        _protocol = Empty(protocol);
        _scheme = Empty(scheme) ?? "https";
    }

    public IReadOnlyList<string> Keys => KeyList;

    public string? Get(string key) =>
        key switch
        {
            KnownTransportKeys.ClientAddress => _clientAddress,

            KnownTransportKeys.ServerAddress => _serverAddress,

            // "HTTP/1.1" on the event; the convention wants the version alone.
            KnownTransportKeys.NetworkProtocolVersion => Version(_protocol),

            // API Gateway serves HTTPS only. An ALB listener can serve plain HTTP, and says so only
            // in X-Forwarded-Proto, which is what the load balancer's event passes in here.
            KnownTransportKeys.UrlScheme => _scheme,

            _ => null,
        };

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? Version(string? protocol)
    {
        if (protocol == null)
        {
            return null;
        }

        var slash = protocol.IndexOf('/');

        return slash > -1 ? protocol.Substring(slash + 1) : protocol;
    }
}
