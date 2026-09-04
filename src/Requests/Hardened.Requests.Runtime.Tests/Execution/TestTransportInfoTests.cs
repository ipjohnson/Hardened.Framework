using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The transport facts a test states outright.
/// </summary>
/// <remarks>
/// What makes it possible to test a forwarded-headers filter or an address-partitioned rate
/// limiter without opening a socket, which is the only reason the harness has one of these.
/// </remarks>
public class TestTransportInfoTests {

    private static TestExecutionRequest Request() =>
        new("GET", "/", null, new SimpleQueryStringCollection((IDictionary<string, string>?)null));


    [Fact]
    public void AStatedValueIsReturned() {
        var info = new TestTransportInfo(
            (KnownTransportKeys.ClientAddress, "203.0.113.7"),
            (KnownTransportKeys.UrlScheme, "https"));

        Assert.Equal("203.0.113.7", info.Get(KnownTransportKeys.ClientAddress));
        Assert.Equal("https", info.Get(KnownTransportKeys.UrlScheme));
    }

    [Fact]
    public void AnUnstatedValueIsNull() {
        var info = new TestTransportInfo((KnownTransportKeys.ClientAddress, "203.0.113.7"));

        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));
    }

    /// <summary>Only what the test stated is published.</summary>
    [Fact]
    public void KeysAreWhatWasStated() {
        var info = new TestTransportInfo((KnownTransportKeys.ClientAddress, "203.0.113.7"));

        Assert.Equal([KnownTransportKeys.ClientAddress], info.Keys);
    }

    /// <summary>
    /// A request that says nothing about its transport still answers.
    /// </summary>
    /// <remarks>
    /// Which is nearly every test. The default is empty rather than null so that a test never has
    /// to set a transport it does not care about.
    /// </remarks>
    [Fact]
    public void TheDefaultIsEmptyRatherThanNull() {
        var request = Request();

        Assert.NotNull(request.Transport);
        Assert.Empty(request.Transport.Keys);
        Assert.Null(request.Transport.Get(KnownTransportKeys.ClientAddress));
    }

    /// <summary>
    /// A fork keeps what the request was told about its transport.
    /// </summary>
    /// <remarks>
    /// Asserted in the conformance suite for every adapter as well; here because the harness is
    /// what a filter test forks, and a fork that lost its transport would make a rate limiter's
    /// partition depend on whether something happened to fork the chain.
    /// </remarks>
    [Fact]
    public void ACloneKeepsTheTransport() {
        var request = Request();

        request.Transport = new TestTransportInfo((KnownTransportKeys.ClientAddress, "203.0.113.7"));

        var clone = request.Clone(method: "DELETE", null, null, null, null);

        Assert.Same(request.Transport, clone.Transport);
    }
}
