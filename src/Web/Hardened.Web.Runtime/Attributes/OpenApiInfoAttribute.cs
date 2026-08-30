namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// The published document's <c>info</c>: its title, version, and optionally a description.
///
/// <para>
/// Without it a code-first application's document is titled after the entry point's class name
/// and versioned "1.0.0", because those are the only facts the generator has. A
/// specification-first application never needs this - its contract carries an <c>info</c> block,
/// and what the contract says wins over this attribute if both are present.
/// </para>
///
/// <code>
/// [HardenedModule]
/// [OpenApiInfo("Consignments API", "1.2.0")]
/// public partial class Application { }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class OpenApiInfoAttribute : Attribute {
    public OpenApiInfoAttribute(string title, string version = "1.0.0", string? description = null) {
        Title = title;
        Version = version;
        Description = description;
    }

    public string Title { get; }

    public string Version { get; }

    public string? Description { get; }
}
