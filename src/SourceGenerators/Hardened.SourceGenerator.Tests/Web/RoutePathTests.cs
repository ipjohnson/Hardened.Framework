using Hardened.SourceGenerator.Web.Routing;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The one place a base path and a route template are joined.
/// </summary>
/// <remarks>
/// Five callers share it — the route tree, the ambiguity diagnostic, the generated links, the
/// served OpenAPI document and the class-level <c>[BasePath]</c> in the handler model. They all
/// concatenated before, which agreed only because they were wrong the same way: the document and
/// the links described <c>/orders/</c> while the matcher answered <c>/orders/</c> too, so a
/// generated client called a URL that worked and the collection's own address did not.
/// </remarks>
public class RoutePathTests {

    [Theory]
    // A template of "/" names the base itself, which is the case that was broken.
    [InlineData("/collection", "/", "/collection")]
    [InlineData("/collection", "", "/collection")]
    [InlineData("/collection/", "/", "/collection")]
    // The ordinary join, with the boundary slash collapsed rather than doubled.
    [InlineData("/collection", "/items", "/collection/items")]
    [InlineData("/collection/", "/items", "/collection/items")]
    [InlineData("/collection", "items", "/collection/items")]
    [InlineData("/collection", "/{id}", "/collection/{id}")]
    [InlineData("/collection", "/{*path}", "/collection/{*path}")]
    // No base path: the template is the route, and "/" is still the root.
    [InlineData("", "/", "/")]
    [InlineData("", "/items", "/items")]
    [InlineData("", "items", "/items")]
    [InlineData(null, "/items", "/items")]
    // A base path of "/" is the identity, not a prefix.
    [InlineData("/", "/", "/")]
    [InlineData("/", "/items", "/items")]
    // A trailing slash the author wrote on a real segment is theirs to keep: /a/ and /a are
    // different URLs under strict matching, and only "/" alone means "no segment of my own".
    [InlineData("/collection", "/items/", "/collection/items/")]
    public void Combine_JoinsTheBasePathToTheTemplate(string? basePath, string template, string expected) {
        Assert.Equal(expected, RoutePath.Combine(basePath, template));
    }

    /// <summary>
    /// Nothing composes to the empty string, because the empty string is not a path a matcher,
    /// a document or a link can use.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData(null, null)]
    public void Combine_NeverProducesAnEmptyPath(string? basePath, string? template) {
        Assert.Equal("/", RoutePath.Combine(basePath, template));
    }
}
