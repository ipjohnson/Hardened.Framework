using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// What Kestrel's features answer about the connection.
/// </summary>
/// <remarks>
/// <para>
/// The keys are OpenTelemetry's, so what these pin is the mapping from a feature onto a published
/// name - the thing that has to be identical here and in every other transport, or an address does
/// not look the same under Lambda as it does here.
/// </para>
/// </remarks>
public class FeatureTransportInfoTests {

    private static FeatureTransportInfo Info(
        IPAddress? remote = null, int remotePort = 0,
        IPAddress? local = null, int localPort = 0,
        string protocol = "HTTP/1.1", string scheme = "https") {
        var connection = Substitute.For<IHttpConnectionFeature>();

        connection.RemoteIpAddress.Returns(remote);
        connection.RemotePort.Returns(remotePort);
        connection.LocalIpAddress.Returns(local);
        connection.LocalPort.Returns(localPort);

        var request = new HttpRequestFeature { Protocol = protocol, Scheme = scheme };

        return new FeatureTransportInfo(connection, request);
    }

    [Fact]
    public void TheClientAddressIsTheSocketPeer() {
        var info = Info(remote: IPAddress.Parse("203.0.113.7"), remotePort: 51234);

        Assert.Equal("203.0.113.7", info.Get(KnownTransportKeys.ClientAddress));
        Assert.Equal("51234", info.Get(KnownTransportKeys.ClientPort));
    }

    /// <summary>
    /// And the peer is published separately, holding the same value.
    /// </summary>
    /// <remarks>
    /// They agree here because nothing sits in front of Kestrel - it is the server the client
    /// reached. The pair exists so that a forwarded-headers filter can replace the client without
    /// destroying the observed fact, which is the only one the transport can vouch for.
    /// </remarks>
    [Fact]
    public void ThePeerIsPublishedSeparatelyFromTheClient() {
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

    /// <summary>
    /// The protocol version is the version, not the whole token.
    /// </summary>
    /// <remarks>
    /// Kestrel reports <c>HTTP/1.1</c>; the semantic convention is <c>1.1</c>. Publishing the token
    /// would mean every consumer strips the prefix, and half of them would forget.
    /// </remarks>
    [Theory]
    [InlineData("HTTP/1.1", "1.1")]
    [InlineData("HTTP/2", "2")]
    [InlineData("HTTP/3", "3")]
    public void TheProtocolVersionDropsTheScheme(string protocol, string expected) {
        Assert.Equal(expected, Info(protocol: protocol).Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    /// <summary>A protocol with no slash is reported as it stands rather than dropped.</summary>
    [Fact]
    public void AProtocolWithNoSlashSurvives() {
        Assert.Equal("2", Info(protocol: "2").Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Fact]
    public void TheSchemeIsSurfaced() {
        Assert.Equal("https", Info(scheme: "https").Get(KnownTransportKeys.UrlScheme));
    }

    /// <summary>
    /// A port of zero is an absence, not a port.
    /// </summary>
    /// <remarks>
    /// Kestrel reports 0 for a connection that has no port - a Unix domain socket, a named pipe -
    /// and publishing "0" would put a number on the wire that no client ever connected from.
    /// </remarks>
    [Fact]
    public void AZeroPortIsNull() {
        var info = Info(remote: IPAddress.Loopback, remotePort: 0, local: IPAddress.Loopback);

        Assert.Null(info.Get(KnownTransportKeys.ClientPort));
        Assert.Null(info.Get(KnownTransportKeys.NetworkPeerPort));
        Assert.Null(info.Get(KnownTransportKeys.ServerPort));
    }

    /// <summary>
    /// A server that supplied no connection feature answers null, not an exception.
    /// </summary>
    /// <remarks>
    /// An in-process harness is one, and so is a test constructing an adapter from a bare request
    /// feature - which several of the conformance tests do.
    /// </remarks>
    [Fact]
    public void NoConnectionFeatureAnswersNull() {
        var info = new FeatureTransportInfo(null, new HttpRequestFeature { Protocol = "HTTP/1.1" });

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.NetworkPeerAddress));
        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));

        // The request feature still answers, because it is not the connection.
        Assert.Equal("1.1", info.Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Fact]
    public void AnUnknownKeyIsNull() {
        Assert.Null(Info().Get("something.else"));
    }

    /// <summary>Every key it publishes is one it understands.</summary>
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
