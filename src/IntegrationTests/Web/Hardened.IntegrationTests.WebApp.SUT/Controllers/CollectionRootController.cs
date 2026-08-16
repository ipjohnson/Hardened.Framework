using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A controller whose base path is itself a resource — the shape every collection API has.
/// </summary>
/// <remarks>
/// <para>
/// <c>[BasePath("/collection")]</c> with <c>[Get("/")]</c> is how you say "the collection lives at
/// the root of my space". It used to compose by string concatenation into <c>/collection/</c>, so
/// the declared collection URL answered nothing and only its trailing-slash spelling worked.
/// </para>
/// <para>
/// A sibling token route is here too, because the fix has to leave <c>/collection/{id}</c> alone:
/// the boundary slash is collapsed, not deleted.
/// </para>
/// </remarks>
[BasePath("/collection")]
public class CollectionRootController {

    [Get("/")]
    public string List() => "collection";

    [Post("/")]
    public string Create() => "created";

    [Get("/{id}")]
    public string Item(string id) => "item:" + id;
}
