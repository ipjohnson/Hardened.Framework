using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>[Enable&lt;T&gt;]</c> and the template bases it produces, on a specification-first
/// application.
///
/// <para>
/// A spec-first application has views for the same reason a code-first one does — an operation
/// answering <c>text/html</c> needs something to render it — so the template base generator is
/// wired into this generator too. What it emits is derived entirely from two declarative facets on
/// the marker, so this suite is what proves the extension point works from the spec-first side
/// rather than only where the code-first tests look.
/// </para>
/// </summary>
public class EnabledFeatureTests {

    private const string Spec =
        """
        openapi: "3.0.0"
        info: { title: Views, version: "1.0" }
        paths:
          /health:
            get:
              tags: [Health]
              operationId: health
              responses:
                '200':
                  description: ok
                  content:
                    text/plain:
                      schema: { type: string }
        """;

    /// <summary>
    /// The entry point carrying <paramref name="enables"/>, compiled alongside a template engine
    /// package's markers and bases — the shape a referenced package ships.
    /// </summary>
    private static SourceGeneration.Testing.GeneratorResult Generate(string enables) =>
        OpenApiGenerator.Run(
            Spec,
            $$"""
              using System;
              using System.Threading.Tasks;
              using Hardened.Requests.Abstract.Execution;
              using Hardened.Requests.Abstract.Outputs;
              using Hardened.Requests.Abstract.Templates;
              using Hardened.Shared.Runtime.Attributes;
              using Other.Engine;

              namespace Other.Engine {
                  [TemplateBase(typeof(EngineHtmlTemplate<>))]
                  [TemplateContentType("text/html")]
                  public sealed class HardenedRazorTemplate { }

                  [TemplateBase(typeof(EngineTextTemplate<>))]
                  [TemplateContentType("text/plain")]
                  public sealed class FluidTemplate { }

                  public sealed class PlainMarker { }

                  // The contract a [TemplateBase] declares: IHardenedResponseOutput, an overridable
                  // ContentType for the generated base to replace, and a protected Context the
                  // generated Links property resolves services from.
                  public abstract class EngineHtmlTemplate<TModel> : IHardenedResponseOutput<TModel> {
                      protected IExecutionContext Context { get; private set; } = default!;

                      public virtual string ContentType => "text/html";

                      public bool SupportsContentType(string? accept, IExecutionContext context) => true;

                      public Task WriteOutput(IExecutionContext context) => Task.CompletedTask;
                  }

                  public abstract class EngineTextTemplate<TModel> : IHardenedResponseOutput<TModel> {
                      protected IExecutionContext Context { get; private set; } = default!;

                      public virtual string ContentType => "text/plain";

                      public bool SupportsContentType(string? accept, IExecutionContext context) => true;

                      public Task WriteOutput(IExecutionContext context) => Task.CompletedTask;
                  }
              }

              namespace TestNamespace {
                  [HardenedModule]
                  {{enables}}
                  public partial class TestApp {
                  }
              }
              """);

    /// <summary>
    /// The base is named from the marker and the entry point together — the entry point supplies
    /// the prefix that scopes it to a module, and the marker supplies the rest with a leading
    /// <c>Hardened</c> stripped.
    /// </summary>
    [Fact]
    public void ATemplateBaseIsNamedFromTheEntryPointAndTheMarker() {
        var result = Generate("[Enable<HardenedRazorTemplate>]").AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, name => name.Contains("TestAppRazorTemplate"));
    }

    /// <summary>
    /// It derives from whatever the marker's <c>[TemplateBase]</c> points at and produces whatever
    /// its <c>[TemplateContentType]</c> says. The generator never learns what either one means,
    /// which is what lets a package supply an engine without a change here.
    /// </summary>
    [Fact]
    public void ATemplateBaseDerivesFromTheMarkersBaseAndCarriesItsContentType() {
        var source = Generate("[Enable<HardenedRazorTemplate>]")
            .AssertNoErrors()
            .SourceContaining("RazorTemplate");

        Assert.Contains("EngineHtmlTemplate", source);
        Assert.Contains("text/html", source);
    }

    /// <summary>
    /// Two markers on one entry point produce two bases rather than one winning. Multi-engine is
    /// the design rather than a retrofit, and a name derived from the marker is what makes it work.
    /// </summary>
    [Fact]
    public void TwoMarkersProduceTwoDistinctlyNamedBases() {
        var result = Generate("[Enable<HardenedRazorTemplate>]\n    [Enable<FluidTemplate>]")
            .AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, name => name.Contains("TestAppRazorTemplate"));
        Assert.Contains(result.GeneratedSources.Keys, name => name.Contains("TestAppFluidTemplate"));
    }

    /// <summary>
    /// A marker with no template base is some other kind of feature and is passed over rather than
    /// reported. One attribute name serves every optional feature, which is the point of it.
    /// </summary>
    [Fact]
    public void AMarkerWithNoTemplateBaseEmitsNoBase() {
        var result = Generate("[Enable<PlainMarker>]").AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, name => name.Contains("Template"));
    }

    /// <summary>
    /// The spellings are one attribute. A spec-first project writing the qualified form and getting
    /// no views would have nothing to look at — no diagnostic, and a base that simply never appears.
    /// </summary>
    [Theory]
    [InlineData("[Enable<HardenedRazorTemplate>]")]
    [InlineData("[EnableAttribute<HardenedRazorTemplate>]")]
    [InlineData("[Hardened.Shared.Runtime.Attributes.Enable<HardenedRazorTemplate>]")]
    public void EverySpellingOfEnableProducesTheBase(string enables) {
        var result = Generate(enables).AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, name => name.Contains("RazorTemplate"));
    }

    /// <summary>An entry point enabling nothing emits no base, which is the ordinary API case.</summary>
    [Fact]
    public void AnEntryPointWithNoFeaturesEmitsNoBase() {
        var result = Generate("").AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, name => name.Contains("Template"));
    }
}
