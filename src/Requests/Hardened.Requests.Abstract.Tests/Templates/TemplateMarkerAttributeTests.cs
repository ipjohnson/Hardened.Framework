using System.Reflection;
using Hardened.Requests.Abstract.Templates;

namespace Hardened.Requests.Abstract.Tests.Templates;

/// <summary>
/// The two attributes a template engine package declares on its feature marker.
///
/// <para>
/// These are the whole extension point. The generator never learns what any particular marker
/// means: it resolves the marker named in <c>[Enable&lt;T&gt;]</c>, reads these two attributes off
/// it, and emits a base deriving from whatever <c>[TemplateBase]</c> names, producing whatever
/// <c>[TemplateContentType]</c> says. A Fluid or Mustache package ships its own marker carrying
/// its own pair and needs no generator change.
/// </para>
///
/// <para>
/// So the position that matters is a consumer's: a marker declared in another assembly, with both
/// attributes applied to it and read back. The markers below stand in for that package.
/// </para>
/// </summary>
public class TemplateMarkerAttributeTests {

    /// <summary>An engine package's marker, as it would ship.</summary>
    [TemplateBase(typeof(FakeTemplateBase<>))]
    [TemplateContentType("text/html")]
    private class HtmlMarker { }

    /// <summary>A second engine producing something else, which is how a new format is added.</summary>
    [TemplateBase(typeof(FakeTemplateBase<>))]
    [TemplateContentType("text/calendar")]
    private class CalendarMarker { }

    private class FakeTemplateBase<TModel> { }

    /// <summary>
    /// The base type survives being read back as an <em>unbound</em> generic. That is the reason
    /// the marker is a separate non-generic type pointing at the base rather than being the base:
    /// <c>typeof(X&lt;&gt;)</c> is legal as an attribute argument, while
    /// <c>[Enable&lt;HardenedHtmlTemplate&lt;&gt;&gt;]</c> is not.
    /// </summary>
    [Fact]
    public void TemplateBaseCarriesAnUnboundGenericBackToTheGenerator() {
        var baseType = typeof(HtmlMarker).GetCustomAttribute<TemplateBaseAttribute>()!.BaseType;

        Assert.Equal(typeof(FakeTemplateBase<>), baseType);
        Assert.True(baseType.IsGenericTypeDefinition);
    }

    /// <summary>
    /// The content type is per marker rather than per template, because it follows from the base
    /// class — a base that escapes its output produces HTML, one that does not produces text. Two
    /// markers naming different types is how a package adds a format.
    /// </summary>
    [Theory]
    [InlineData(typeof(HtmlMarker), "text/html")]
    [InlineData(typeof(CalendarMarker), "text/calendar")]
    public void TemplateContentTypeCarriesWhatTheMarkersTemplatesProduce(Type marker, string expected) {
        Assert.Equal(expected, marker.GetCustomAttribute<TemplateContentTypeAttribute>()!.ContentType);
    }

    /// <summary>
    /// Both are declared for one class at a time and are not inherited. A marker is a leaf: a base
    /// deriving from another engine's marker and quietly picking up its content type is exactly the
    /// confusion this extension point exists to avoid.
    /// </summary>
    [Theory]
    [InlineData(typeof(TemplateBaseAttribute))]
    [InlineData(typeof(TemplateContentTypeAttribute))]
    public void BothMarkerAttributesApplyToOneClassAndAreNotInherited(Type attributeType) {
        var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
