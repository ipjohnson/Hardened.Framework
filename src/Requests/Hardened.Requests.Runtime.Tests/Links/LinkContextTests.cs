using Hardened.Requests.Runtime.Links;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Links;

/// <summary>
/// The default <see cref="Hardened.Requests.Abstract.Links.ILinkContext"/>, which turns a route
/// into something a client can call.
///
/// <para>
/// It exists because a route and a link are different questions on the framework's primary host.
/// API Gateway strips the stage before the application sees the path, so a root-relative link built
/// from the route alone 404s. Every case below is a way that goes wrong: a base path configured
/// with the slash the route also carries, or a scheme with no host to pair it with.
/// </para>
/// </summary>
public class LinkContextTests {

    private static LinkContext Context(string basePath = "", string? scheme = null, string? host = null) =>
        new(Options.Create<ILinkConfiguration>(
            new LinkConfiguration { BasePath = basePath, Scheme = scheme, Host = host }));

    /// <summary>
    /// The common host serves at the root, and a link is then the route unchanged rather than the
    /// route with an empty prefix concatenated onto it.
    /// </summary>
    [Fact]
    public void NoBasePathLeavesTheRouteAlone() {
        Assert.Equal("/orders/7", Context().Resolve("/orders/7"));
    }

    /// <summary>
    /// A base path is prefixed. This is the API Gateway stage: without it every link the
    /// application generates is missing the segment the host stripped.
    /// </summary>
    [Fact]
    public void ABasePathIsPrefixedToTheRoute() {
        Assert.Equal("/prod/orders/7", Context("/prod").Resolve("/orders/7"));
    }

    /// <summary>
    /// Configured either way, the result is the same. A route always starts with a slash, so a base
    /// path that keeps its own would compose into a double slash — which is a different URL, and on
    /// some proxies a redirect.
    /// </summary>
    [Theory]
    [InlineData("/prod")]
    [InlineData("/prod/")]
    public void ATrailingSlashOnTheBasePathIsTrimmed(string basePath) {
        Assert.Equal("/prod/orders", Context(basePath).Resolve("/orders"));
    }

    /// <summary>
    /// With both halves of an origin, an absolute link is the origin and the resolved path — base
    /// path included, because an absolute link is a resolved one with a prefix rather than a
    /// separate construction.
    /// </summary>
    [Fact]
    public void AnAbsoluteLinkUsesTheSchemeHostAndBasePath() {
        Assert.Equal(
            "https://api.example.com/prod/orders",
            Context("/prod", "https", "api.example.com").Absolute("/orders"));
    }

    /// <summary>
    /// Half an origin is not an origin. A scheme with no host, or a host with no scheme, falls back
    /// to the relative link rather than producing <c>https:///orders</c> or <c>://host/orders</c> —
    /// strings that look like links and resolve nowhere.
    /// </summary>
    [Theory]
    [InlineData("https", null)]
    [InlineData(null, "api.example.com")]
    [InlineData("https", "")]
    [InlineData("", "api.example.com")]
    [InlineData(null, null)]
    public void HalfAnOriginFallsBackToTheRelativeLink(string? scheme, string? host) {
        Assert.Equal("/prod/orders", Context("/prod", scheme, host).Absolute("/orders"));
    }

    /// <summary>The configured values are readable, which is what a host replacing this reads.</summary>
    [Fact]
    public void TheConfiguredOriginIsReadBack() {
        var context = Context("/prod/", "https", "api.example.com");

        Assert.Equal("/prod", context.BasePath);
        Assert.Equal("https", context.Scheme);
        Assert.Equal("api.example.com", context.Host);
    }

    /// <summary>
    /// The default configuration serves at the root with no origin, which is right for Kestrel and
    /// ASP.NET Core.
    /// </summary>
    [Fact]
    public void TheDefaultConfigurationServesAtTheRoot() {
        var configuration = new LinkConfiguration();

        Assert.Equal("", configuration.BasePath);
        Assert.Null(configuration.Scheme);
        Assert.Null(configuration.Host);
    }
}
