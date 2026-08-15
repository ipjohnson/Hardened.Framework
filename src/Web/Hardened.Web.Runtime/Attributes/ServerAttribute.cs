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
/// Applied to the entry point, and more than once where an application is served from several
/// places:
/// </para>
///
/// <code>
/// [HardenedModule]
/// [Server("https://api.example.com", "Production")]
/// [Server("https://staging.example.com", "Staging")]
/// public partial class Application { }
/// </code>
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
