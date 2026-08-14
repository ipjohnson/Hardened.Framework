using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Templates;

/// <summary>
/// Renders a named template to the response.
/// </summary>
/// <remarks>
/// <para>
/// The seam that <c>[Template]</c> was always pointing at. <c>TemplateAttribute</c> puts a name on
/// <c>IExecutionResponse.TemplateName</c> before the handler runs and nothing read it back once the
/// Mustache engine was removed in #101; an implementation of this interface is what reads it.
/// </para>
/// <para>
/// Deliberately says nothing about Razor. The model arrives as <see cref="object"/> because the
/// name is resolved at runtime, so an engine that needs a typed model closes that gap on its own
/// side - see <c>Hardened.Templates.RazorBlade</c>, which does it with a generated-free registry of
/// statically typed factories rather than reflection.
/// </para>
/// <para>
/// Engines are resolved as a set and tested in order. The first one that claims a name renders it,
/// which is what lets an application register a second engine for a subset of its views.
/// </para>
/// </remarks>
public interface ITemplateEngine {
    /// <summary>
    /// Whether this engine knows the named template. Called before every render, and also used to
    /// decide whether the response is a template response at all, so it must not throw for an
    /// unknown name.
    /// </summary>
    bool CanRender(string templateName);

    /// <summary>
    /// What the named template renders as, or null if this engine does not know the template.
    /// </summary>
    /// <remarks>
    /// Asked before rendering, because the media type a template produces is what decides whether it
    /// is what the client wanted. It varies per template rather than per engine - the same RazorBlade
    /// engine serves <c>text/html</c> views and <c>text/plain</c> ones - which is why this is a
    /// lookup rather than a property on the engine.
    /// </remarks>
    string? ContentTypeFor(string templateName);

    /// <summary>
    /// Renders the template to <c>context.Response.Body</c> and sets the response content type.
    /// </summary>
    Task RenderAsync(string templateName, object? model, IExecutionContext context);
}
