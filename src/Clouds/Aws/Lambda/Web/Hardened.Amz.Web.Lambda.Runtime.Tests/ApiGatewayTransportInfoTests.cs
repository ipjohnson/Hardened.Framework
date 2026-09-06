using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// What API Gateway can say about the caller.
/// </summary>
/// <remarks>
/// The keys are the framework's, which are OpenTelemetry's, so what these pin is the mapping from
/// a gateway event onto a published name - the thing that has to agree with the Kestrel adapter or
/// an address does not read the same under Lambda as it does on a server.
/// </remarks>
public class ApiGatewayTransportInfoTests {

    private static ApiGatewayTransportInfo Info(
        string? sourceIp = "203.0.113.7",
        string? protocol = "HTTP/1.1",
        string? domainName = "api.example.com") =>
        new(new APIGatewayHttpApiV2ProxyRequest {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                DomainName = domainName,
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                    SourceIp = sourceIp,
                    Protocol = protocol
                }
            }
        });

    /// <summary>
    /// The client address is the caller, already behind the intermediary.
    /// </summary>
    /// <remarks>
    /// The one place this transport answers better than a self-hosted one. API Gateway terminates
    /// the connection and reports the caller it saw, so there is nothing for a forwarded-headers
    /// filter to correct - where Kestrel has to answer with its socket peer and wait.
    /// </remarks>
    [Fact]
    public void TheClientAddressIsTheGatewaysSourceIp() {
        Assert.Equal("203.0.113.7", Info().Get(KnownTransportKeys.ClientAddress));
    }

    /// <summary>
    /// The socket peer is null, because the function is never told it.
    /// </summary>
    /// <remarks>
    /// The immediate peer of this process is API Gateway itself and its address is not in the
    /// event. Answering with the source IP would claim an observation nothing made, and a caller
    /// comparing the two to detect a proxy would conclude there was none.
    /// </remarks>
    [Fact]
    public void ThePeerAddressIsNullBecauseTheGatewayIsThePeer() {
        Assert.Null(Info().Get(KnownTransportKeys.NetworkPeerAddress));
        Assert.Null(Info().Get(KnownTransportKeys.NetworkPeerPort));
    }

    [Fact]
    public void TheServerAddressIsTheDomainName() {
        Assert.Equal("api.example.com", Info().Get(KnownTransportKeys.ServerAddress));
    }

    /// <summary>Matching the Kestrel adapter: the version, not the whole token.</summary>
    [Theory]
    [InlineData("HTTP/1.1", "1.1")]
    [InlineData("HTTP/2", "2")]
    public void TheProtocolVersionDropsTheScheme(string protocol, string expected) {
        Assert.Equal(expected, Info(protocol: protocol).Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    /// <summary>
    /// The scheme is always https, and is not read off the event.
    /// </summary>
    /// <remarks>
    /// API Gateway does not serve plaintext, and there is nothing on the request that could say
    /// otherwise - so this is a fact about the product rather than about the request.
    /// </remarks>
    [Fact]
    public void TheSchemeIsAlwaysHttps() {
        Assert.Equal("https", Info().Get(KnownTransportKeys.UrlScheme));
    }

    /// <summary>
    /// A field the gateway omitted is null, not empty.
    /// </summary>
    /// <remarks>
    /// The event is deserialized from JSON, so an omitted field arrives null and an empty one
    /// arrives as "". Both mean the same thing, and the framework contract is null.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentFieldIsNull(string? sourceIp) {
        Assert.Null(Info(sourceIp: sourceIp).Get(KnownTransportKeys.ClientAddress));
    }

    /// <summary>An event with no request context at all does not throw.</summary>
    [Fact]
    public void AnEventWithNoRequestContextAnswersNull() {
        var info = new ApiGatewayTransportInfo(new APIGatewayHttpApiV2ProxyRequest());

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));
        Assert.Null(info.Get(KnownTransportKeys.NetworkProtocolVersion));

        // Still https: it is a fact about the product, not about this event.
        Assert.Equal("https", info.Get(KnownTransportKeys.UrlScheme));
    }

    /// <summary>
    /// A request context with no <c>http</c> block answers null rather than throwing.
    /// </summary>
    /// <remarks>
    /// Between "no context at all" and "a fully populated one" there is a partly-populated one,
    /// and it is the shape a hand-written test event usually has. The null-conditional chain
    /// covers it; this is what says so.
    /// </remarks>
    [Fact]
    public void ARequestContextWithNoHttpBlockAnswersNull() {
        var info = new ApiGatewayTransportInfo(new APIGatewayHttpApiV2ProxyRequest {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                DomainName = "api.example.com"
            }
        });

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.NetworkProtocolVersion));

        // The half that is present still answers.
        Assert.Equal("api.example.com", info.Get(KnownTransportKeys.ServerAddress));
    }

    /// <summary>An absent domain name is null, the same as an absent source IP.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentDomainNameIsNull(string? domainName) {
        Assert.Null(Info(domainName: domainName).Get(KnownTransportKeys.ServerAddress));
    }

    /// <summary>A protocol with no slash is reported as it stands.</summary>
    [Fact]
    public void AProtocolWithNoSlashSurvives() {
        Assert.Equal("2", Info(protocol: "2").Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentProtocolIsNull(string? protocol) {
        Assert.Null(Info(protocol: protocol).Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Fact]
    public void AnUnknownKeyIsNull() {
        Assert.Null(Info().Get("something.else"));
    }

    [Fact]
    public void EveryPublishedKeyIsAnswerable() {
        var info = Info();

        Assert.NotEmpty(info.Keys);

        foreach (var key in info.Keys) {
            Assert.NotNull(info.Get(key));
        }
    }

    /// <summary>
    /// A fork keeps the transport it was forked from.
    /// </summary>
    /// <remarks>
    /// The framework's conformance suite asserts this for every adapter it covers, and these three
    /// are not enrolled in it yet - so it is asserted here directly. Identity rather than equality,
    /// because that is the property a caller relies on.
    /// </remarks>
    [Fact]
    public async Task ACloneKeepsTheTransport() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event(sourceIp: "203.0.113.7"));

        var request = harness.ExecutionContext.Request;

        var clone = request.Clone(method: "DELETE");

        Assert.Same(request.Transport, clone.Transport);
        Assert.Equal("203.0.113.7", clone.Transport.Get(KnownTransportKeys.ClientAddress));
    }

    /// <summary>
    /// And the transport reaches the request through the real event processor.
    /// </summary>
    /// <remarks>
    /// The unit tests above build the info directly. This is what says the mapper actually hands
    /// the proxy request to it - the wiring, rather than the mapping.
    /// </remarks>
    [Fact]
    public async Task TheTransportReachesTheRequest() {
        var harness = new ApiGatewayHarness();

        await harness.Process(
            ApiGatewayHarness.Event(sourceIp: "198.51.100.4", domainName: "api.example.com"));

        var transport = harness.ExecutionContext.Request.Transport;

        Assert.Equal("198.51.100.4", transport.Get(KnownTransportKeys.ClientAddress));
        Assert.Equal("api.example.com", transport.Get(KnownTransportKeys.ServerAddress));
    }
}
