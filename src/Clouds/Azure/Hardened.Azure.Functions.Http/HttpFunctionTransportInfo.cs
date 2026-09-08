using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker.Http;

namespace Hardened.Azure.Functions.Http;

/// <summary>
/// The connection, as the worker describes it.
/// </summary>
/// <remarks>
/// <para>
/// The worker sits behind the host, and the host behind whatever fronts it, so what this can say
/// honestly is what the request carries: the scheme and host of the URL the host built, and the
/// client address only where a proxy in front wrote it into <c>X-Forwarded-For</c>, which is what
/// the Functions host does for the caller it saw.
/// </para>
/// <para>
/// <c>network.peer.address</c> is deliberately absent. The worker's peer is the host on a local
/// channel, and the host does not say what its own peer was.
/// </para>
/// </remarks>
public sealed class HttpFunctionTransportInfo : ITransportInfo {
    private static readonly string[] KeyList = [
        KnownTransportKeys.ClientAddress,
        KnownTransportKeys.ServerAddress,
        KnownTransportKeys.ServerPort,
        KnownTransportKeys.UrlScheme
    ];

    private readonly HttpRequestData _request;

    public HttpFunctionTransportInfo(HttpRequestData request) {
        _request = request;
    }

    public IReadOnlyList<string> Keys => KeyList;

    public string? Get(string key) =>
        key switch {
            KnownTransportKeys.ClientAddress => ForwardedFor(),
            KnownTransportKeys.ServerAddress => Empty(_request.Url.Host),
            KnownTransportKeys.ServerPort => _request.Url.IsDefaultPort ? null : _request.Url.Port.ToString(),
            KnownTransportKeys.UrlScheme => Empty(_request.Url.Scheme),
            _ => null
        };

    /// <summary>The first address in <c>X-Forwarded-For</c>, which is the caller the proxy saw.</summary>
    private string? ForwardedFor() {
        if (!_request.Headers.TryGetValues("X-Forwarded-For", out var values)) {
            return null;
        }

        var first = values.FirstOrDefault();

        if (string.IsNullOrEmpty(first)) {
            return null;
        }

        var comma = first!.IndexOf(',');
        var address = (comma < 0 ? first : first.Substring(0, comma)).Trim();

        // The host writes the port beside the address, which the convention keeps separate.
        var colon = address.LastIndexOf(':');

        if (colon > 0 && address.IndexOf(':') == colon) {
            address = address.Substring(0, colon);
        }

        return Empty(address);
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
