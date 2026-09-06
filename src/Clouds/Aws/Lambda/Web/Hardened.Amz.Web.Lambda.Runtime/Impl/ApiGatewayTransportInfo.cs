using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// The connection, as API Gateway describes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This transport answers <see cref="KnownTransportKeys.ClientAddress"/> better than a
/// self-hosted one can.</b> API Gateway terminates the connection and reports
/// <c>requestContext.http.sourceIp</c> as the caller it saw - so the address here is already the
/// client behind the intermediary, which is exactly what the convention asks for. Kestrel has to
/// answer with its socket peer and wait for a forwarded-headers filter to correct it; there is
/// nothing to correct here.
/// </para>
/// <para>
/// <b><see cref="KnownTransportKeys.NetworkPeerAddress"/> is deliberately null.</b> The immediate
/// peer of this process is API Gateway itself, and the function is never told its address. Null is
/// the honest answer, and answering with the source IP would claim an observation nothing made.
/// </para>
/// <para>
/// The bag takes any key, so AWS-specific facts - the API Gateway request id, the stage, the api
/// id - can be published here without a change to the framework. Left out for now because the
/// request id is already the correlation id and the rest have no consumer.
/// </para>
/// </remarks>
public sealed class ApiGatewayTransportInfo : ITransportInfo {
    private static readonly string[] KeyList = [
        KnownTransportKeys.ClientAddress,
        KnownTransportKeys.ServerAddress,
        KnownTransportKeys.NetworkProtocolVersion,
        KnownTransportKeys.UrlScheme
    ];

    private readonly APIGatewayHttpApiV2ProxyRequest _request;

    public ApiGatewayTransportInfo(APIGatewayHttpApiV2ProxyRequest request) {
        _request = request;
    }

    public IReadOnlyList<string> Keys => KeyList;

    public string? Get(string key) =>
        key switch {
            KnownTransportKeys.ClientAddress => Empty(_request.RequestContext?.Http?.SourceIp),

            KnownTransportKeys.ServerAddress => Empty(_request.RequestContext?.DomainName),

            // "HTTP/1.1" from the gateway; the convention wants the version alone.
            KnownTransportKeys.NetworkProtocolVersion =>
                Version(_request.RequestContext?.Http?.Protocol),

            // API Gateway does not serve plaintext, so this is not read off the request - there is
            // nothing on it that could say otherwise.
            KnownTransportKeys.UrlScheme => "https",

            _ => null
        };

    /// <summary>
    /// Null for an absent value, because the framework contract is null rather than empty.
    /// </summary>
    /// <remarks>
    /// The proxy request is deserialized from JSON, so a field the gateway omitted arrives as null
    /// and a field it sent empty arrives as "". Both mean the same thing here.
    /// </remarks>
    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? Version(string? protocol) {
        if (string.IsNullOrEmpty(protocol)) {
            return null;
        }

        var slash = protocol!.IndexOf('/');

        return slash > -1 ? protocol.Substring(slash + 1) : protocol;
    }
}
