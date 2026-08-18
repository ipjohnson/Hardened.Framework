using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// What ASP.NET Core answers about the connection.
/// </summary>
/// <remarks>
/// The same keys and the same answers as the Kestrel adapter, which is the point: a caller reading
/// <c>client.address</c> must not have to know which host it is running under. Where the two
/// genuinely differ is noted on the test that shows it.
/// </remarks>
public class AspNetTransportInfoTests {

    private static AspNetTransportInfo Info(
        IPAddress? remote = null, int remotePort = 0,
        IPAddress? local = null, int localPort = 0,
        string protocol = "HTTP/1.1", string scheme = "https") {
        var context = new DefaultHttpContext();

        context.Connection.RemoteIpAddress = remote;
        context.Connection.RemotePort = remotePort;
        context.Connection.LocalIpAddress = local;
        context.Connection.LocalPort = localPort;
        context.Request.Protocol = protocol;
        context.Request.Scheme = scheme;

        return new AspNetTransportInfo(context.Request);
    }

    [Fact]
    public void TheClientAddressComesFromTheConnection() {
        var info = Info(remote: IPAddress.Parse("203.0.113.7"), remotePort: 51234);

        Assert.Equal("203.0.113.7", info.Get(KnownTransportKeys.ClientAddress));
        Assert.Equal("51234", info.Get(KnownTransportKeys.ClientPort));
    }

    /// <summary>
    /// The peer holds the same value, and on this host that is a weaker statement than on Kestrel.
    /// </summary>
    /// <remarks>
    /// ASP.NET's own <c>ForwardedHeadersMiddleware</c> rewrites <c>RemoteIpAddress</c> in place
    /// when an application enables it, so by the time <c>UseHardened()</c> runs the "peer" may
    /// already be the forwarded client. There is no second value to read - the host overwrote it -
    /// and reporting what the host believes is the only answer available. Recorded here because it
    /// is a real difference between the two adapters rather than an oversight.
    /// </remarks>
    [Fact]
    public void ThePeerReportsWhateverTheHostBelieves() {
        var info = Info(remote: IPAddress.Parse("203.0.113.7"), remotePort: 51234);

        Assert.Equal("203.0.113.7", info.Get(KnownTransportKeys.NetworkPeerAddress));
        Assert.Equal("51234", info.Get(KnownTransportKeys.NetworkPeerPort));
    }

    [Fact]
    public void TheServerAddressComesFromTheLocalEndpoint() {
        var info = Info(local: IPAddress.Parse("10.0.0.4"), localPort: 443);

        Assert.Equal("10.0.0.4", info.Get(KnownTransportKeys.ServerAddress));
        Assert.Equal("443", info.Get(KnownTransportKeys.ServerPort));
    }

    [Theory]
    [InlineData("HTTP/1.1", "1.1")]
    [InlineData("HTTP/2", "2")]
    [InlineData("HTTP/3", "3")]
    public void TheProtocolVersionDropsTheScheme(string protocol, string expected) {
        Assert.Equal(expected, Info(protocol: protocol).Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Fact]
    public void TheSchemeIsSurfaced() {
        Assert.Equal("http", Info(scheme: "http").Get(KnownTransportKeys.UrlScheme));
    }

    /// <summary>A port of zero is an absence, matching the Kestrel adapter.</summary>
    [Fact]
    public void AZeroPortIsNull() {
        var info = Info(remote: IPAddress.Loopback, remotePort: 0, local: IPAddress.Loopback);

        Assert.Null(info.Get(KnownTransportKeys.ClientPort));
        Assert.Null(info.Get(KnownTransportKeys.ServerPort));
    }

    /// <summary>A context with no addresses answers null rather than throwing.</summary>
    [Fact]
    public void AnUnpopulatedConnectionAnswersNull() {
        var info = Info();

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));
    }

    [Fact]
    public void AnUnknownKeyIsNull() {
        Assert.Null(Info().Get("something.else"));
    }

    [Fact]
    public void EveryPublishedKeyIsAnswerable() {
        var info = Info(
            remote: IPAddress.Parse("203.0.113.7"), remotePort: 51234,
            local: IPAddress.Parse("10.0.0.4"), localPort: 443);

        Assert.NotEmpty(info.Keys);

        foreach (var key in info.Keys) {
            Assert.NotNull(info.Get(key));
        }
    }
}
