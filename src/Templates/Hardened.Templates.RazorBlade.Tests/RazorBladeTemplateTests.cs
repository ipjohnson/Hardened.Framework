using Hardened.Templates.RazorBlade.Tests.Models;
using Xunit;

namespace Hardened.Templates.RazorBlade.Tests;

/// <summary>
/// The descriptor factories, and the model mismatches they are there to name.
/// </summary>
public class RazorBladeTemplateTests {

    private static readonly FortunePage Page = new([new Fortune(1, "hello")]);

    [Fact]
    public void Html_CarriesTheHtmlContentType() {
        var descriptor = RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model));

        Assert.Equal("Fortunes", descriptor.Name);
        Assert.Equal(RazorBladeTemplate.HtmlContentType, descriptor.ContentType);
    }

    [Fact]
    public void PlainText_CarriesThePlainTextContentType() {
        var descriptor = RazorBladeTemplate.PlainText<FortunePage>("Receipt", model => new Views.Receipt(model));

        Assert.Equal(RazorBladeTemplate.PlainTextContentType, descriptor.ContentType);
    }

    /// <summary>
    /// Anything else - a CSV, an SVG, an iCal feed - without needing a method per media type.
    /// </summary>
    [Fact]
    public void Create_TakesAnArbitraryContentType() {
        var descriptor = RazorBladeTemplate.Create<FortunePage>(
            "Export", "text/csv", model => new Views.Receipt(model));

        Assert.Equal("text/csv", descriptor.ContentType);
    }

    [Fact]
    public void Create_BuildsTheTemplateFromTheModel() {
        var descriptor = RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model));

        Assert.IsType<Views.Fortunes>(descriptor.Create(Page));
    }

    /// <summary>
    /// Without this the cast fails somewhere inside the factory with an InvalidCastException that
    /// names two model types and no template.
    /// </summary>
    [Fact]
    public void Create_AModelOfTheWrongTypeNamesTheTemplateAndBothTypes() {
        var descriptor = RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model));

        var exception = Assert.Throws<InvalidOperationException>(() => descriptor.Create("not a page"));

        Assert.Contains("Fortunes", exception.Message);
        Assert.Contains(nameof(FortunePage), exception.Message);
        Assert.Contains(nameof(String), exception.Message);
    }

    /// <summary>
    /// A handler returning null against a template typed for a value type would otherwise throw
    /// NullReferenceException from inside the cast.
    /// </summary>
    [Fact]
    public void Create_ANullModelForAValueTypeNamesTheTemplate() {
        var descriptor = RazorBladeTemplate.Create<int>("Count", "text/plain", _ => new Views.Receipt(Page));

        var exception = Assert.Throws<InvalidOperationException>(() => descriptor.Create(null));

        Assert.Contains("Count", exception.Message);
    }

    /// <summary>
    /// A reference-typed model is allowed to be null - a view of "nothing found" is a view.
    /// </summary>
    [Fact]
    public void Create_ANullModelForAReferenceTypeIsAllowed() {
        var descriptor = RazorBladeTemplate.Html<FortunePage?>("Fortunes", _ => new Views.Fortunes(Page));

        Assert.NotNull(descriptor.Create(null));
    }

    [Fact]
    public void Create_RejectsAnEmptyName() {
        Assert.Throws<ArgumentException>(
            () => RazorBladeTemplate.Html<FortunePage>("", model => new Views.Fortunes(model)));
    }

    [Fact]
    public void Create_RejectsAnEmptyContentType() {
        Assert.Throws<ArgumentException>(
            () => RazorBladeTemplate.Create<FortunePage>("Fortunes", "", model => new Views.Fortunes(model)));
    }

    [Fact]
    public void Create_RejectsANullFactory() {
        Assert.Throws<ArgumentNullException>(
            () => RazorBladeTemplate.Html<FortunePage>("Fortunes", null!));
    }
}
