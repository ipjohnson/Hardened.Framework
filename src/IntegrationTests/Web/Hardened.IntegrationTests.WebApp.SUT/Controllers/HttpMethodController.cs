using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// DELETE and PATCH routes, driven through the generated routing table.
///
/// The generator has recognised both verbs since the first commit, and the runtime routes them -
/// what was missing was the attribute itself. <c>DeleteAttribute</c> and <c>PatchAttribute</c> were
/// empty internal stubs that did not derive from <see cref="Attribute"/>, so no project could apply
/// them and nothing here could regress. These routes exist so that cannot happen again silently.
/// </summary>
[BasePath("/verbs")]
public class HttpMethodController {

    [Delete("/item/{id}")]
    public string DeleteItem(string id) => $"deleted:{id}";

    /// <summary>Confirms DELETE reaches the other binding sources, not just the path.</summary>
    [Delete("/item")]
    public string DeleteByQuery([FromQueryString] string name) => $"deleted:{name}";

    [Patch("/item/{id}")]
    public string PatchItem(string id, MathAddModel model) =>
        $"patched:{id}:{string.Join(",", model.Values ?? new List<int>())}";

    /// <summary>
    /// Same path as <see cref="DeleteItem"/>, different verb. The route tree has to keep the two
    /// apart rather than matching on path alone.
    /// </summary>
    [Get("/item/{id}")]
    public string GetItem(string id) => $"got:{id}";

    /// <summary>
    /// A hand-written handler declaring the status it answers with.
    /// </summary>
    /// <remarks>
    /// The other half of what a described operation states with a <c>responses:</c> key. Both reach
    /// <c>ResponseInformationModel.DefaultStatusCode</c>, so this and the specification-first route
    /// exercise one runtime behaviour rather than two. <c>SuccessStatus</c> was removed from these
    /// attributes on 2026-08-11 because nothing read it; this is what reading it looks like.
    /// </remarks>
    [Post("/created", SuccessStatus = 201)]
    public string CreateItem() => "created";

    /// <summary>A declared 204, which also means the body is not written.</summary>
    [Delete("/emptied", SuccessStatus = 204)]
    public string EmptyItem() => "this body is not written";
}
