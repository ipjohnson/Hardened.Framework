using RazorBlade;

namespace Hardened.Templates.RazorBlade;

/// <summary>
/// One named template, and how to build it from an untyped model.
/// </summary>
/// <remarks>
/// <para>
/// The factory is where the generic gap closes. A template name is resolved at runtime, so the
/// model arrives as <see cref="object"/>, but a RazorBlade template is <c>HtmlTemplate&lt;TModel&gt;</c>
/// and takes its model through a constructor. Something has to bridge those, and the two obvious
/// ways are both wrong for this framework: reflection over the generic type is AOT-hostile, and
/// making the engine generic pushes the type parameter back out to a call site that does not have
/// it either.
/// </para>
/// <para>
/// A delegate closed over a static cast costs neither. <see cref="RazorBladeTemplate"/> builds these
/// so the cast is written once per template by the compiler rather than by hand.
/// </para>
/// </remarks>
public sealed class RazorBladeTemplateDescriptor {
    private readonly Func<object?, RazorTemplate> _factory;

    public RazorBladeTemplateDescriptor(string name, string contentType, Func<object?, RazorTemplate> factory) {
        Name = name;
        ContentType = contentType;
        _factory = factory;
    }

    /// <summary>
    /// The name a handler asks for, matched case-insensitively.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What the rendered output is, taken from the template's base type rather than guessed from a
    /// file extension.
    /// </summary>
    public string ContentType { get; }

    public RazorTemplate Create(object? model) => _factory(model);
}
