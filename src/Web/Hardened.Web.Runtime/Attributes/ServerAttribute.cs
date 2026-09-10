namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// A base URL the application is served from, written into the document's <c>servers</c> list.
///
/// <para>
/// The one thing in a generated document that cannot be derived from the code: where the
/// application is deployed. Without it a client generated from the document has a set of paths and
/// nowhere to send them, so every consumer configures the host separately and the document is not
/// quite the whole contract.
/// </para>
///
/// <para>
/// Applied to the <c>[HardenedModule]</c> class in the compilation that writes the document, and
/// more than once where an application is served from several places:
/// </para>
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [Server("https://api.example.com", "Production")]
/// [Server("https://staging.example.com", "Staging")]
/// public partial class Catalog { }
/// </code>
///
/// <para>
/// <b>Which class, when there are two.</b> The document is written where the routes are, which in
/// the layout the templates ship is the library rather than the host: a project with a
/// <c>Catalog</c> module and a <c>Catalog.Host</c> application publishes from the library, and the
/// attribute on the host's <c>Application</c> reaches a compilation that writes no document. It is
/// read off the module class's own attribute list, so a handler class is not a placement either -
/// <c>HOAG032</c> says so on a described handler.
/// </para>
///
/// <para>
/// <b>The assembly target is not read.</b> <c>AttributeTargets.Assembly</c> is on the usage below
/// and the document writer takes the entry point's class attributes only, so
/// <c>[assembly: Server(...)]</c> compiles and publishes nothing.
/// </para>
///
/// <para>
/// A specification-first application says it in the contract instead. A <c>servers</c> block there
/// is published as written, and wins over this attribute where both exist, which is the precedence
/// <c>info</c> already has.
/// </para>
///
/// <para>
/// Not the same thing as <c>[BasePath]</c>, and deliberately not derived from it. A base path is
/// already part of every path the document writes; repeating it here would make a
/// specification-first build reading the server URL apply it a second time.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public class ServerAttribute : Attribute {
    public ServerAttribute(string url, string? description = null) {
        Url = url;
        Description = description;
    }

    public string Url { get; }

    public string? Description { get; }
}
