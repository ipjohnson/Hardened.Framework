using RazorBlade;

namespace Hardened.Templates.RazorBlade;

/// <summary>
/// Builds <see cref="RazorBladeTemplateDescriptor"/> values without writing the cast by hand.
/// </summary>
/// <remarks>
/// The content type comes from which method was called, which is the same thing as the template's
/// base type: <c>HtmlTemplate</c> escapes its output and <c>PlainTextTemplate&lt;T&gt;</c> does not.
/// That replaces the old template path's <c>// todo: update to get content type from template
/// extension</c> with something the compiler already knows.
/// </remarks>
public static class RazorBladeTemplate {
    public const string HtmlContentType = "text/html; charset=utf-8";

    public const string PlainTextContentType = "text/plain; charset=utf-8";

    /// <summary>
    /// An HTML template. <paramref name="factory"/> is normally <c>model => new Views.Fortunes(model)</c>.
    /// </summary>
    public static RazorBladeTemplateDescriptor Html<TModel>(
        string name, Func<TModel, HtmlTemplate> factory) =>
        Create(name, HtmlContentType, factory);

    /// <summary>
    /// A plain-text template, for a view whose output must not be HTML-escaped.
    /// </summary>
    public static RazorBladeTemplateDescriptor PlainText<TModel>(
        string name, Func<TModel, RazorTemplate> factory) =>
        Create(name, PlainTextContentType, factory);

    /// <summary>
    /// A template whose content type is neither of the above - <c>text/csv</c>, an SVG, an iCal feed.
    /// </summary>
    public static RazorBladeTemplateDescriptor Create<TModel>(
        string name, string contentType, Func<TModel, RazorTemplate> factory) {
        ArgumentNullException.ThrowIfNull(factory);

        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A template name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(contentType)) {
            throw new ArgumentException("A content type is required.", nameof(contentType));
        }

        return new RazorBladeTemplateDescriptor(name, contentType, model => {
            // A null model reaching a template typed for a value type would otherwise surface as a
            // NullReferenceException from inside the cast, with no indication of which template.
            if (model is null && default(TModel) is not null) {
                throw new InvalidOperationException(
                    $"Template '{name}' needs a {typeof(TModel).Name} model but the response value was null.");
            }

            if (model is not null && model is not TModel) {
                throw new InvalidOperationException(
                    $"Template '{name}' needs a {typeof(TModel).Name} model but the response value was " +
                    $"{model.GetType().Name}.");
            }

            return factory((TModel)model!);
        });
    }
}
