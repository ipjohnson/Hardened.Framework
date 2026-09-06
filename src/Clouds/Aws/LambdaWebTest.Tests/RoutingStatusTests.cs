using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Shared.Lambda.Testing;
using LambdaWebTest;
using Xunit;

namespace LambdaWebTest.Tests;

/// <summary>
/// The whole buffered API Gateway stack, end to end: a generated <c>Application</c>, the real
/// middleware chain, the real routing table.
///
/// <para>
/// This exists because nothing did. Every test in the repository asked for a route that exists, and
/// <c>LambdaWebTest</c>'s only route is <c>/{author}/{name}</c>, which matches almost anything — so
/// no test ever asked for a path that should not match, and the transport returned an empty 200 for
/// all of them from the first release until 2026-08-15. A client could not tell "no such endpoint"
/// from "succeeded with no content".
/// </para>
/// </summary>
public class RoutingStatusTests {
    private static readonly Application _application = new();

    internal static APIGatewayHttpApiV2ProxyRequest Request(string path, string method = "GET") =>
        new() {
            RawPath = path,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                    Method = method,
                    Path = path
                }
            },
            Headers = new Dictionary<string, string>()
        };

    [Fact]
    public async Task Invoke_UnmatchedPathReturns404() {
        var response = await _application.Invoke(
            Request("/nope"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Invoke_UnmatchedPathAtTheRootReturns404() {
        var response = await _application.Invoke(Request("/"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.Equal(404, response.StatusCode);
    }

    /// <summary>
    /// An unmatched route must not come back as success no matter how deep it is.
    ///
    /// <para>
    /// The paths here are deliberately one segment: <c>/{author}/{name}</c> matches anything with
    /// two <em>or more</em> segments, because the generated routing table's trailing token consumes
    /// the rest of the span including slashes — <c>/a/b/c/d</c> binds <c>name = "b/c/d"</c>. That is
    /// a defect in <c>Hardened.Web.SourceGenerator</c>, not in this transport, so it is not asserted
    /// here; it is only the reason a deep path is not a valid "unmatched" fixture.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Invoke_UnmatchedPathReturnsNoBody() {
        var response = await _application.Invoke(
            Request("/nope"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.Equal(404, response.StatusCode);
        Assert.True(string.IsNullOrEmpty(response.Body));
    }

    /// <summary>
    /// A method the route does not declare is still not a match, and must not come back as success.
    /// </summary>
    [Fact]
    public async Task Invoke_UnmatchedMethodOnAMatchedPathDoesNotReturn200() {
        var response = await _application.Invoke(
            Request("/some-author/some-name", "DELETE"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.NotEqual(200, response.StatusCode);
    }

    [Fact]
    public async Task Invoke_MatchedRouteReturns200() {
        var response = await _application.Invoke(
            Request("/some-author/some-name"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.Equal(200, response.StatusCode);
    }
}
