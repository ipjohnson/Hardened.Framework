using CSharpAuthor;
using Hardened.SourceGenerator.Links;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Templates;

/// <summary>
/// Emits one abstract template base per <c>[Enable&lt;T&gt;]</c> marker on an entry point.
/// </summary>
/// <remarks>
/// <para>
/// The RazorBlade coupling lives in <c>Hardened.Templates.RazorBlade</c>, which already references
/// it. What is emitted here names no third-party type of its own - it derives from whatever the
/// marker's <c>[TemplateBase]</c> points at:
/// </para>
/// <code>
/// public abstract class ApplicationRazorTemplate&lt;TModel&gt; : HardenedHtmlTemplate&lt;TModel&gt; {
///     public override string ContentType => "text/html; charset=utf-8";
/// }
/// </code>
/// <para>
/// <b>The name derives from the marker</b>, so two markers on one module produce
/// <c>ApplicationRazorTemplate&lt;T&gt;</c> and <c>ApplicationFluidTemplate&lt;T&gt;</c> -
/// multi-engine by design rather than retrofitted. The entry point supplies the prefix, which is
/// what scopes the type to a module: an assembly with two entry points would otherwise have two
/// generators racing for one name.
/// </para>
/// <para>
/// The generator never asks what the marker <em>is</em>. It reads two declarative facets and emits
/// from them, which is what lets another package supply a template engine without a change here.
/// </para>
/// </remarks>
public static class TemplateBaseGenerator {

    /// <summary>The facet naming the class a generated base derives from.</summary>
    public const string BaseFacet = "TemplateBase";

    /// <summary>The facet naming what templates on that base produce.</summary>
    public const string ContentTypeFacet = "TemplateContentType";

    /// <summary>
    /// Markers stripped of this prefix when the generated name is derived, so Hardened's own
    /// <c>HardenedRazorTemplate</c> produces <c>ApplicationRazorTemplate</c> rather than
    /// <c>ApplicationHardenedRazorTemplate</c>. A third-party marker names itself and keeps its
    /// name.
    /// </summary>
    private const string MarkerPrefix = "Hardened";

    private const string ModelParameter = "TModel";

    public static void Generate(SourceProductionContext context, EntryPointSelector.Model appModel) {
        foreach (var feature in appModel.EnabledFeatures) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var baseType = feature.Facet(BaseFacet)?.TypeValue;

            // A marker with no template base is some other kind of feature. Not an error: one
            // attribute name serves every optional feature, which is the point of it.
            if (baseType == null) {
                continue;
            }

            context.AddSource(
                appModel.EntryPointType.Name + "." + TypeName(appModel, feature),
                Write(appModel, feature, baseType));
        }
    }

    /// <summary>
    /// What a view names in its <c>@inherits</c> directive.
    /// </summary>
    public static string TypeName(EntryPointSelector.Model appModel, EnabledFeatureModel feature) {
        var marker = feature.MarkerType.Name;

        if (marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) && marker.Length > MarkerPrefix.Length) {
            marker = marker.Substring(MarkerPrefix.Length);
        }

        return appModel.EntryPointType.Name + marker;
    }

    private static string Write(
        EntryPointSelector.Model appModel, EnabledFeatureModel feature, ITypeDefinition baseType) {
        var file = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        var definition = file.AddClass(TypeName(appModel, feature));

        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Abstract;
        definition.AddGenericParameter(ModelParameter);
        definition.Comment =
            $"The base a {appModel.EntryPointType.Name} view derives from with @inherits. " +
            $"Generated from [Enable<{feature.MarkerType.Name}>].";

        // Closed over this class's own parameter, so a view writing
        // @inherits ApplicationRazorTemplate<FortunePage> gets HardenedHtmlTemplate<FortunePage>
        // and its typed Model.
        definition.AddBaseType(new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            baseType.Namespace,
            baseType.Name,
            new ITypeDefinition[] { new TypeParameterDefinition(ModelParameter) }));

        WriteLinks(appModel, definition);

        var contentType = feature.Facet(ContentTypeFacet)?.Value;

        // Only when the marker states one. The base class already answers with what it produces,
        // and overriding it with the same string would be noise.
        if (!string.IsNullOrEmpty(contentType)) {
            var property = definition.AddProperty(typeof(string), "ContentType");

            property.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
            property.Set = null;
            property.Get.LambdaSyntax = true;
            property.Get.AddCode("\"" + contentType!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\";");
        }

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        file.WriteOutput(outputContext);

        return outputContext.Output();
    }

    /// <summary>
    /// The module's links, on every view that derives from this base.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where links pay off. RazorBlade compiles <c>.cshtml</c> at build time, copies
    /// <c>@</c> expressions verbatim without analysing them, and emits <c>#line</c> directives with
    /// exact spans - so <c>@Links.Products.GetProduct(Model.Id)</c> breaks the template at compile
    /// time, at its own line and column, when the route changes. Rails' <c>product_path</c> and
    /// Flask's <c>url_for</c> are runtime lookups; the same mistake fails when someone loads the
    /// page.
    /// </para>
    /// <para>
    /// Reached through <c>IHardenedTemplate.Context</c> rather than a member of whichever base the
    /// marker names, so this works for a template engine the generator has never seen. Resolved on
    /// first use, because most views do not link.
    /// </para>
    /// </remarks>
    private static void WriteLinks(EntryPointSelector.Model appModel, ClassDefinition definition) {
        var linksType = LinkGenerator.LinksType(appModel);
        var qualified = "global::" + linksType.Namespace + "." + linksType.Name;

        var backing = definition.AddField(linksType.MakeNullable(), "_links");

        backing.Modifiers |= ComponentModifier.Private;

        var property = definition.AddProperty(linksType, "Links");

        property.Modifiers |= ComponentModifier.Public;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(
            "_links ??= global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions" +
            ".GetRequiredService<" + qualified + ">(" +
            "((global::" + KnownTypes.Requests.IHardenedTemplate.Namespace + "." +
            KnownTypes.Requests.IHardenedTemplate.Name + ")this).Context.RequestServices);");
    }
}
