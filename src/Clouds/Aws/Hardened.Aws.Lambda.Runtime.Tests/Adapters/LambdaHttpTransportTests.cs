using Amazon.Lambda.APIGatewayEvents;
using Hardened.Aws.Lambda.Http;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Adapters;

/// <summary>
/// The transport info, and the request and response clones, read without running a chain.
/// </summary>
public class LambdaHttpTransportTests
{
    // ------------------------------------------------------------------ transport info

    [Fact]
    public void PublishesWhatTheEventDescribes()
    {
        var info = new LambdaHttpTransportInfo(
            Proxy(sourceIp: "203.0.113.7", domainName: "api.example.com", protocol: "HTTP/1.1")
        );

        Assert.Equal("203.0.113.7", info.Get(KnownTransportKeys.ClientAddress));
        Assert.Equal("api.example.com", info.Get(KnownTransportKeys.ServerAddress));
        Assert.Equal("1.1", info.Get(KnownTransportKeys.NetworkProtocolVersion));
        Assert.Equal("https", info.Get(KnownTransportKeys.UrlScheme));
    }

    [Fact]
    public void AProtocolWithNoSlashIsTheVersion()
    {
        var info = new LambdaHttpTransportInfo(Proxy(protocol: "2"));

        Assert.Equal("2", info.Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentOrEmptyValueIsNull(string? value)
    {
        var info = new LambdaHttpTransportInfo(
            Proxy(sourceIp: value, domainName: value, protocol: value)
        );

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));
        Assert.Null(info.Get(KnownTransportKeys.NetworkProtocolVersion));
    }

    [Fact]
    public void AnEventWithNoRequestContextAnswersNull()
    {
        var info = new LambdaHttpTransportInfo(new APIGatewayHttpApiV2ProxyRequest());

        Assert.Null(info.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(info.Get(KnownTransportKeys.ServerAddress));
        Assert.Null(info.Get(KnownTransportKeys.NetworkProtocolVersion));
        Assert.Equal("https", info.Get(KnownTransportKeys.UrlScheme));
    }

    [Fact]
    public void TheNetworkPeerIsNotPublished()
    {
        var info = new LambdaHttpTransportInfo(Proxy(sourceIp: "203.0.113.7"));

        Assert.Null(info.Get(KnownTransportKeys.NetworkPeerAddress));
        Assert.DoesNotContain(KnownTransportKeys.NetworkPeerAddress, info.Keys);
        Assert.Equal(4, info.Keys.Count);
    }

    // ------------------------------------------------------------------ request clone

    [Fact]
    public void ARequestClonedBeforeItsHeadersAreReadReadsTheEventsHeaders()
    {
        var proxy = Proxy();
        proxy.Headers = new Dictionary<string, string> { ["x-trace"] = "abc" };
        var request = new LambdaHttpRequest(proxy, new MemoryStream());

        var clone = request.Clone();

        Assert.Equal("abc", (string?)clone.Headers["x-trace"]);
    }

    [Fact]
    public void AHeaderSetOnAClonedRequestDoesNotReachTheOriginal()
    {
        var proxy = Proxy();
        proxy.Headers = new Dictionary<string, string> { ["x-trace"] = "abc" };
        var request = new LambdaHttpRequest(proxy, new MemoryStream());
        _ = request.Headers;

        var clone = request.Clone();
        clone.Headers["x-trace"] = "def";

        Assert.Equal("abc", (string?)request.Headers.Get("x-trace"));
        Assert.Equal("def", (string?)clone.Headers["x-trace"]);
    }

    // ------------------------------------------------------------------ response clone

    [Fact]
    public void AResponseCloneCarriesItsState()
    {
        Func<IExecutionContext, IHardenedResponseOutput> factory = _ =>
            throw new InvalidOperationException();
        var response = new LambdaHttpResponse(new MemoryStream())
        {
            ResponseValue = "value",
            OutputFactory = factory,
            IsBinary = true,
            ShouldSerialize = false,
            Status = 418,
        };
        response.Headers.Set("x-original", "1");

        var clone = (LambdaHttpResponse)response.Clone();

        Assert.Same(response.Body, clone.Body);
        Assert.Equal("value", clone.ResponseValue);
        Assert.Same(factory, clone.OutputFactory);
        Assert.True(clone.IsBinary);
        Assert.False(clone.ShouldSerialize);
        Assert.Equal(418, clone.Status);
        Assert.False(clone.Headers.ContainsKey("x-original"));
    }

    [Fact]
    public void AResponseCloneTakesTheSuppliedHeaders()
    {
        var response = new LambdaHttpResponse(new MemoryStream()) { ContentType = "text/plain" };
        var headers = new HeaderCollectionStringValues(
            new Dictionary<string, StringValues> { ["x-supplied"] = "yes" }
        );

        var clone = response.Clone(headers);

        Assert.Equal("yes", (string?)clone.Headers["x-supplied"]);
        Assert.Equal("text/plain", response.ContentType);
    }

    [Fact]
    public void AStatusSetOnAResponseCloneDoesNotReachTheOriginal()
    {
        var response = new LambdaHttpResponse(new MemoryStream()) { Status = 200 };

        var clone = (LambdaHttpResponse)response.Clone(null);
        clone.Status = 500;

        Assert.Equal(200, response.Status);
    }

    // ------------------------------------------------------------------ helpers

    private static APIGatewayHttpApiV2ProxyRequest Proxy(
        string? sourceIp = null,
        string? domainName = null,
        string? protocol = null
    ) =>
        new()
        {
            RawPath = "/orders",
            Version = "2.0",
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                DomainName = domainName,
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription
                {
                    Method = "GET",
                    SourceIp = sourceIp,
                    Protocol = protocol,
                },
            },
        };
}
