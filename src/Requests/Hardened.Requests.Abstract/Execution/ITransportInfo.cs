namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// What the transport knows about the connection a request arrived on.
/// </summary>
/// <remarks>
/// <para>
/// Key-value rather than properties, deliberately. Every transport knows a different subset -
/// Kestrel has a socket, API Gateway has a request context, an in-memory harness has whatever a
/// test gave it - and a property per fact would mean an interface that grows every time a host is
/// added, with most of it null on most of them. A bag says "ask, and get nothing if this transport
/// cannot answer", which is the truth.
/// </para>
/// <para>
/// <b>Lazy by contract.</b> <see cref="Get"/> is called, not a dictionary built - so a host that
/// would have to reach into a connection feature to answer only does it when something asks. Most
/// requests ask nothing.
/// </para>
/// <para>
/// <b>Keys are the OpenTelemetry semantic conventions</b>, not names of this framework's invention -
/// see <see cref="KnownTransportKeys"/>. That is what makes the same fact look the same under
/// Lambda as under Kestrel, and it is the vocabulary the request pipeline already tags spans and
/// metrics with.
/// </para>
/// </remarks>
public interface ITransportInfo {
    /// <summary>
    /// The value for <paramref name="key"/>, or null when this transport cannot answer it.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, because "this transport has no concept of a socket peer" and "the
    /// peer address is the empty string" are different answers and only one of them is ever true.
    /// </remarks>
    string? Get(string key);

    /// <summary>
    /// The keys this transport can answer, for diagnostics that want to show everything.
    /// </summary>
    /// <remarks>
    /// A key appearing here does not promise a non-null <see cref="Get"/> - a Kestrel connection
    /// over a Unix socket has no remote port. It promises the transport understands the key.
    /// </remarks>
    IReadOnlyList<string> Keys { get; }
}

/// <summary>
/// The keys a transport is expected to answer, where it can.
/// </summary>
/// <remarks>
/// <para>
/// These are OpenTelemetry's HTTP semantic conventions verbatim. Reusing them rather than inventing
/// <c>ipAddress</c> buys two things: a published definition for each one, settled by people who
/// thought about proxies; and one vocabulary in this codebase, since the pipeline already tags
/// spans with <c>http.request.method</c>, <c>url.path</c> and <c>http.response.status_code</c>.
/// </para>
/// <para>
/// <b><see cref="ClientAddress"/> and <see cref="NetworkPeerAddress"/> are the pair that matters</b>,
/// and the distinction is the whole reason an address is hard behind a proxy. The peer is who
/// opened the socket - which behind API Gateway, an ALB or CloudFront is the proxy. The client is
/// who made the request, which is only knowable from what the proxy said. A transport answers the
/// peer because it observed it; a forwarded-headers filter answers the client because it trusted
/// something.
/// </para>
/// </remarks>
public static class KnownTransportKeys {
    /// <summary>
    /// Who made the request, behind any intermediaries.
    /// </summary>
    /// <remarks>
    /// A transport sets this to the peer when nothing sits in front, because then they are the same
    /// thing. Behind a proxy it is only correct once something has read the forwarded headers, and
    /// a transport that has not must leave it null rather than answer with the proxy's address.
    /// </remarks>
    public const string ClientAddress = "client.address";

    /// <summary>The port <see cref="ClientAddress"/> connected from, where it is known.</summary>
    public const string ClientPort = "client.port";

    /// <summary>Who opened the socket - the proxy, when there is one.</summary>
    public const string NetworkPeerAddress = "network.peer.address";

    /// <summary>The port the peer connected from.</summary>
    public const string NetworkPeerPort = "network.peer.port";

    /// <summary>The address the request was addressed to, as the server sees it.</summary>
    public const string ServerAddress = "server.address";

    /// <summary>The port the server accepted on.</summary>
    public const string ServerPort = "server.port";

    /// <summary>The HTTP version - <c>1.1</c>, <c>2</c>, <c>3</c>.</summary>
    public const string NetworkProtocolVersion = "network.protocol.version";

    /// <summary><c>http</c> or <c>https</c>, as the transport saw it.</summary>
    public const string UrlScheme = "url.scheme";
}

/// <summary>
/// A transport that knows nothing about its connection.
/// </summary>
/// <remarks>
/// The honest answer for an in-memory harness and for a queue or stream record, where there is no
/// connection to describe. A singleton, because it holds nothing and most requests never ask.
/// </remarks>
public class EmptyTransportInfo : ITransportInfo {
    public static readonly EmptyTransportInfo Instance = new();

    private EmptyTransportInfo() { }

    public string? Get(string key) => null;

    public IReadOnlyList<string> Keys { get; } = Array.Empty<string>();
}
