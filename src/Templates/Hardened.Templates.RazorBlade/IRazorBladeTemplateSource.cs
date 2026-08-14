namespace Hardened.Templates.RazorBlade;

/// <summary>
/// Supplies the templates an application wants reachable by name.
/// </summary>
/// <remarks>
/// <para>
/// Registration lives in the application rather than here, and not only by preference: RazorBlade
/// generates template classes as <c>internal</c> by default (<c>RazorBladeDefaultAccessibility</c>),
/// so nothing outside that assembly can name them.
/// </para>
/// <para>
/// Implement it, annotate the implementation as a singleton, and return one descriptor per view:
/// <code>
/// [SingletonService]
/// public class AppTemplates : IRazorBladeTemplateSource {
///     public IEnumerable&lt;RazorBladeTemplateDescriptor&gt; Templates =&gt; [
///         RazorBladeTemplate.Html&lt;FortunePage&gt;("Fortunes", model =&gt; new Views.Fortunes(model))
///     ];
/// }
/// </code>
/// </para>
/// <para>
/// Every registered source is merged, so a library can ship views alongside the application's own.
/// </para>
/// </remarks>
public interface IRazorBladeTemplateSource {
    IEnumerable<RazorBladeTemplateDescriptor> Templates { get; }
}
