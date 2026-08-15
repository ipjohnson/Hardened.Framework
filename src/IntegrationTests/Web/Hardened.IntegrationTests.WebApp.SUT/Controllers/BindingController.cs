using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Every parameter binding source, exercised through the routing and binding code the
/// generator emits rather than by constructing parameters directly.
/// </summary>
[BasePath("/binding")]
public class BindingController {

    [Get("/path/{id}")]
    public string FromPath(string id) => id;

    [Get("/pair/{first}/{second}")]
    public string FromMultiplePathTokens(string first, string second) => $"{first}:{second}";

    /// <summary>
    /// Deliberately overlaps /path/{id} above: same prefix, a path token in the same
    /// position, but a different token name. The route tree stores the token name on the
    /// node rather than the route, so both routes share whichever name was registered first
    /// and this one cannot bind. See OverlappingRouteTokenNamesTests.
    /// </summary>
    [Get("/path/{first}/{second}")]
    public string OverlappingPathTokens(string first, string second) => $"{first}:{second}";

    [Get("/path-typed/{count}")]
    public int TypedPathToken(int count) => count * 2;

    /// <summary>
    /// The same shape with the constraint written into the route. <c>/path-typed/abc</c> reaches
    /// the handler above and answers 400 - the route matched and the binder failed. Here it is a
    /// 404, which is the truthful answer: there is no resource at that URL.
    /// </summary>
    [Get("/path-constrained/{count:int}")]
    public int ConstrainedPathToken(int count) => count * 2;

    /// <summary>
    /// A catch-all token, which binds the same way a constrained one does - by the name the token
    /// declares rather than by the text between the braces.
    /// </summary>
    [Get("/files/{*path}")]
    public string CatchAllToken(string path) => path;

    /// <summary>A constraint this application declares itself, compiled to a direct call.</summary>
    [Get("/path-code/{code:code}")]
    public string CustomConstrainedToken(string code) => code;

    [RouteConstraint("code")]
    public static bool IsCode(ReadOnlySpan<char> value) {
        if (value.Length != 3) {
            return false;
        }

        foreach (var character in value) {
            if (character < 'A' || character > 'Z') {
                return false;
            }
        }

        return true;
    }

    [Get("/query")]
    public string FromQuery([FromQueryString] string name) => name;

    [Get("/query-named")]
    public string FromNamedQuery([FromQueryString("q")] string search) => search;

    [Get("/query-typed")]
    public int TypedQuery([FromQueryString] int page) => page + 1;

    [Get("/cookie")]
    public string FromCookie([FromCookie] string session) => session;

    [Get("/cookie-named")]
    public string FromNamedCookie([FromCookie("theme")] string preference) => preference;

    [Get("/header")]
    public string FromHeader([FromHeader("X-Tenant")] string tenant) => tenant;

    /// <summary>
    /// Path token, query string, header and an injected service in a single handler - the
    /// case most likely to break if binding order or index tracking regresses.
    /// </summary>
    [Get("/mixed/{id}")]
    public string Mixed(
        string id,
        [FromQueryString] string filter,
        [FromHeader("X-Tenant")] string tenant,
        IMathService<int> mathService) {
        return $"{id}|{filter}|{tenant}|{mathService.Add(1, 2)}";
    }

    /// <summary>Body plus a path token, to confirm the two sources coexist.</summary>
    [Post("/body/{label}")]
    public string BodyWithPath(string label, MathAddModel model) =>
        $"{label}:{string.Join(",", model.Values ?? new List<int>())}";
}
