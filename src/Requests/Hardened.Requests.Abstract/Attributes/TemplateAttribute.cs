using Hardened.Requests.Abstract.Templates;

namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The view a handler's result is rendered through.
///
/// <code>
/// [Get("/fortunes")]
/// [Template&lt;Views.Fortunes&gt;]
/// public FortunePage GetFortunes() => _repository.Load();
/// </code>
///
/// <para>
/// A type rather than a name. Because the attribute is applied in the application's own assembly,
/// RazorBlade's <c>internal</c> generated template classes are nameable here - which is the exact
/// problem a registry of named descriptors existed to work around.
/// </para>
///
/// <para>
/// <b>What the constraint buys.</b> <c>[Template&lt;Views.Fortunes&gt;]</c> is your own source,
/// bound in the final compilation where RazorBlade's output exists, so a type that does not
/// implement <see cref="IHardenedTemplate"/> or lacks a parameterless constructor is an error
/// <em>on the attribute</em>, naming the template.
/// </para>
///
/// <para>
/// <b>What it cannot buy.</b> That the template's model matches the handler's return type. The
/// attribute cannot express it - it does not know the return type - and the generator cannot check
/// it, because the template is another generator's output and invisible to it. The generator emits
/// an assignment the compiler has to bind instead; see the <c>_templateCheck_</c> field beside each
/// handler.
/// </para>
///
/// <para>
/// <b>One construction shape only.</b> C# has <c>where T : new()</c> but no constraint for "has a
/// constructor taking <c>TModel</c>", so a second shape could not be compile-checked and would not
/// deliver the guarantee that makes this worth having. The model is attached after construction.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TemplateAttribute<TTemplate> : Attribute
    where TTemplate : IHardenedTemplate, new() { }
