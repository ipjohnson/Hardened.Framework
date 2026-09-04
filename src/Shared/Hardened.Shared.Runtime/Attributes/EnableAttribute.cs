namespace Hardened.Shared.Runtime.Attributes;

/// <summary>
/// Turns on an optional generated feature for this entry point.
///
/// <code>
/// [HardenedModule]
/// [Enable&lt;HardenedRazorTemplates&gt;]
/// public partial class Application { }
/// </code>
///
/// <para>
/// One attribute name for every optional feature, so the question is never "which <c>Enable…</c>
/// was it" - type <c>[Enable&lt;</c> and let completion list what the project has referenced.
/// </para>
///
/// <para>
/// <b>It replaces detection.</b> To write <c>[Enable&lt;HardenedRazorTemplates&gt;]</c> you have to
/// be able to name <c>HardenedRazorTemplates</c>, so the package is referenced or your own code does
/// not compile. There is nothing left for a generator to probe for: no
/// <c>GetTypeByMetadataName</c>, no <c>CompilationProvider</c> gate, and no incrementality concern
/// from having one. Same principle as <c>[Template&lt;T&gt;]</c> - name the type and let the
/// compiler enforce the reference.
/// </para>
///
/// <para>
/// This is not a chicken-and-egg. The marker lives in a referenced assembly and is present the
/// moment the <c>PackageReference</c> resolves. A cycle would exist only if the probed type were
/// generated into the same compilation, which is a different arrangement entirely.
/// </para>
///
/// <para>
/// <b>It says which module.</b> The routing table is already per entry point
/// (<c>Application.Routing</c>), and generated template bases and link types need the same scoping.
/// Without a signal, a generator facing an assembly with two entry points has to guess or collide.
/// </para>
///
/// <para>
/// <b>A marker may also be a DependencyModules module.</b> The constraint is <c>new()</c>, which is
/// what a module needs anyway, and a marker carrying <c>[DependencyModule]</c> has its registrations
/// applied to this entry point as well - so a feature that ships services and a generated type is
/// one attribute rather than two. Ordering differs slightly from writing the module's own
/// attribute: the registrations arrive with the other generated ones rather than in the position
/// the attribute was written in, which matters only for a module deliberately overriding a
/// registration from another.
/// </para>
/// </summary>
/// <typeparam name="TFeature">
/// The feature marker. A marker carries what the generator needs to emit as attributes on itself -
/// <c>[TemplateBase]</c>, <c>[TemplateContentType]</c> - so a package supplying a new template
/// engine needs no generator change at all.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public class EnableAttribute<TFeature> : Attribute where TFeature : new() { }
