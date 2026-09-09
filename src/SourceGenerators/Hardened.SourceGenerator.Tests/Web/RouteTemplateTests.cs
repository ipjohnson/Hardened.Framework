using Hardened.SourceGenerator.Web.Routing;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// A route read by something that is not the router: the document's path template, the constraint
/// guarding one token, and whether there is any constraint at all.
/// </summary>
public class RouteTemplateTests {

    [Theory]
    [InlineData("/todos", "/todos")]
    [InlineData("/todos/{id}", "/todos/{id}")]
    [InlineData("/todos/{id:int}", "/todos/{id}")]
    [InlineData("/todos/{id:int:min(1)}", "/todos/{id}")]
    [InlineData("/files/{*path}", "/files/{path}")]
    [InlineData("/files/{*path:slug}", "/files/{path}")]
    [InlineData("/a/{x:int}/b/{y:guid}/c", "/a/{x}/b/{y}/c")]
    // Malformed templates are the route token diagnostics' to report, not this one's to repair.
    [InlineData("/todos/{id", "/todos/{id")]
    public void NamesOnlyKeepsTheNameAndNothingElse(string path, string template) {
        Assert.Equal(template, RouteTemplate.NamesOnly(path));
    }

    [Theory]
    [InlineData("/todos/{id:int}", "id", "int")]
    [InlineData("/todos/{id:int:min(1)}", "id", "int:min(1)")]
    [InlineData("/todos/{id}", "id", "")]
    [InlineData("/files/{*path:slug}", "path", "slug")]
    [InlineData("/a/{x:int}/b/{y:guid}", "y", "guid")]
    // A parameter that binds from the query or a header is not in the route, and nothing guards it.
    [InlineData("/todos/{id:int}", "limit", "")]
    // By whole name: a prefix is a different token, and reading its constraint as this one's would
    // delete a 400 the shorter token can still answer.
    [InlineData("/todos/{identifier:int}", "id", "")]
    [InlineData("/todos/{id}/{identifier:int}", "id", "")]
    public void ConstraintOnAnswersForOneToken(string path, string token, string chain) {
        Assert.Equal(chain, RouteTemplate.ConstraintOn(path, token));
    }

    [Theory]
    [InlineData("/todos", false)]
    [InlineData("/todos/{id}", false)]
    [InlineData("/todos/{id:int}", true)]
    [InlineData("/a/{x}/b/{y:guid}", true)]
    [InlineData("/todos/{id", false)]
    public void HasConstraintAsksTheWholeRoute(string path, bool constrained) {
        Assert.Equal(constrained, RouteTemplate.HasConstraint(path));
    }
}
