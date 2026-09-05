using Hardened.Requests.Abstract.Templates;

namespace Hardened.Templates.RazorBlade;

/// <summary>
/// The feature marker that turns on generated RazorBlade template bases for a module.
///
/// <code>
/// [HardenedModule]
/// [Enable&lt;RazorTemplates&gt;]
/// public partial class Application { }
/// </code>
///
/// <para>
/// Naming it is what references the package, so there is nothing for a generator to detect. What
/// it produces is an abstract base per module - <c>ApplicationRazorTemplates&lt;TModel&gt;</c> - for
/// a view to declare with <c>@inherits</c>.
/// </para>
///
/// <para>
/// It carries its behaviour declaratively rather than being recognised by name. The generator
/// resolves whichever marker was named, reads these two attributes and emits from them, so a Fluid
/// or Mustache package ships its own marker and needs no generator change. A marker recognised by
/// name would make every new engine a generator change and the extensibility fictional.
/// </para>
///
/// <para>
/// It is a separate non-generic type pointing at the base rather than being the base, because
/// <c>typeof(X&lt;&gt;)</c> is legal in an attribute argument and an unbound generic as a type
/// argument - <c>[Enable&lt;HardenedHtmlTemplate&lt;&gt;&gt;]</c> - is not.
/// </para>
/// </summary>
[TemplateBase(typeof(HardenedHtmlTemplate<>))]
[TemplateContentType("text/html; charset=utf-8")]
public sealed class RazorTemplates { }
