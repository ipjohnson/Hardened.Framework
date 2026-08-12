namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// Which files become templates, what they are named, and how the application finds them at run
/// time.
///
/// <para>
/// Discovery is the part of the generator a consuming project cannot see going wrong: a template
/// that is silently not picked up produces no error, only a <c>Could not locate template named X</c>
/// exception on the first request that needs it.
/// </para>
/// </summary>
public class TemplateDiscoveryTests {

    /// <summary>
    /// The generator compiles four extensions. Anything else in <c>AdditionalFiles</c> is left
    /// alone — an OpenAPI spec and a template reach their generators through the same channel.
    /// </summary>
    [Theory]
    [InlineData("html")]
    [InlineData("js")]
    [InlineData("css")]
    [InlineData("md")]
    public void EveryCompiledExtensionProducesATemplateClass(string extension) {
        var result = TemplateGeneration
            .Generate(($"Asset.{extension}", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors();

        Assert.Contains("Generated.Template_Asset.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// An additional file the generator does not own produces nothing. Sharing
    /// <c>AdditionalFiles</c> with other generators means claiming too much breaks them.
    /// </summary>
    [Theory]
    [InlineData("txt")]
    [InlineData("json")]
    [InlineData("yaml")]
    [InlineData("xml")]
    [InlineData("cshtml")]
    public void AnUnownedExtensionProducesNoTemplateClass(string extension) {
        var result = TemplateGeneration
            .Generate(($"Other.{extension}", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors();

        Assert.DoesNotContain("Generated.Template_Other.cs", result.GeneratedSources.Keys);
    }

    /// <summary>An unowned file alongside a real template does not disturb the template.</summary>
    [Fact]
    public void AnUnownedFileAlongsideATemplateIsIgnored() {
        var result = TemplateGeneration
            .Generate(
                ("Notes.txt", "not a template"),
                ("Page.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors();

        Assert.Contains("Generated.Template_Page.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Generated.Template_Notes.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The template name is the file name without its extension, and that is the string
    /// <c>ITemplateExecutionService.Execute</c> is called with.
    /// </summary>
    [Fact]
    public void ATemplateIsNamedAfterItsFileWithoutTheExtension() {
        var provider = TemplateGeneration
            .Generate(("Welcome.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("case \"Welcome\":", provider);
    }

    /// <summary>Every template in the project gets its own case in the lookup switch.</summary>
    [Fact]
    public void EveryTemplateGetsACaseInTheLookupSwitch() {
        var provider = TemplateGeneration
            .Generate(
                ("Alpha.html", "{{model TestApp.Person}}{{Name}}"),
                ("Beta.html", "{{model TestApp.Person}}{{Age}}"),
                ("Gamma.md", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("case \"Alpha\":", provider);
        Assert.Contains("case \"Beta\":", provider);
        Assert.Contains("case \"Gamma\":", provider);
    }

    /// <summary>
    /// Cases are emitted in a stable order regardless of the order the build hands the files over.
    /// Without that, the generated file churns between builds and every incremental compile is a
    /// diff.
    /// </summary>
    [Fact]
    public void LookupCasesAreEmittedInAStableOrder() {
        var forwards = TemplateGeneration
            .Generate(
                ("Alpha.html", "{{model TestApp.Person}}{{Name}}"),
                ("Beta.html", "{{model TestApp.Person}}{{Name}}"),
                ("Gamma.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        var backwards = TemplateGeneration
            .Generate(
                ("Gamma.html", "{{model TestApp.Person}}{{Name}}"),
                ("Beta.html", "{{model TestApp.Person}}{{Name}}"),
                ("Alpha.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Equal(forwards, backwards);
    }

    /// <summary>
    /// An unknown template name returns null rather than throwing, so the execution service can move
    /// on to the next provider. A provider that threw would make a template in a referenced package
    /// unreachable.
    /// </summary>
    [Fact]
    public void AnUnknownTemplateNameFallsThroughToNull() {
        var provider = TemplateGeneration
            .Generate(("Only.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("return null;", provider);
    }

    /// <summary>
    /// Handlers are cached per template, so a second request for the same template reuses the
    /// instance rather than reconstructing it — and reconstructing it would re-run the escape-service
    /// lookup in <c>Initialize</c> every time.
    /// </summary>
    [Fact]
    public void ATemplateHandlerIsConstructedOnceAndCached() {
        var provider = TemplateGeneration
            .Generate(("Cached.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("_instance_Cached ??= new", provider);
    }

    /// <summary>
    /// The provider is registered against the application class, which is how it reaches the DI
    /// container without the consumer writing any registration code.
    /// </summary>
    [Fact]
    public void TheTemplateProviderIsRegisteredAsASingleton() {
        var provider = TemplateGeneration
            .Generate(("Registered.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("AddSingleton<", provider);
        Assert.Contains("ITemplateExecutionHandlerProvider", provider);
        Assert.Contains("TestApplication.TemplateProvider", provider);
    }

    /// <summary>
    /// The registration hangs off a static field the DI generator picks up. It carries
    /// <c>[DynamicDependency]</c> so the trimmer keeps the method alive — this framework targets
    /// AOT, where a trimmed registration method is a run-time failure, not a build one.
    /// </summary>
    [Fact]
    public void TheRegistrationMethodIsKeptAliveForTrimming() {
        var provider = TemplateGeneration
            .Generate(("Trimmed.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateProvider();

        Assert.Contains("[DynamicDependency(nameof(HardenedTemplateDI))]", provider);
        Assert.Contains("DependencyRegistry<TestApplication>.Add(HardenedTemplateDI)", provider);
    }

    /// <summary>
    /// The provider partial is emitted onto the application class even when the project has no
    /// templates, so a project that adds its first template does not change the surrounding wiring.
    /// </summary>
    [Fact]
    public void TheProviderIsEmittedEvenWithNoTemplates() {
        var result = TemplateGeneration.Generate().AssertNoErrors();

        Assert.Contains("TestApplication.Templates.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Nothing at all is emitted without a <c>[HardenedModule]</c> class: the module is the anchor
    /// every registration hangs off, so there is nowhere to put the provider.
    /// </summary>
    [Fact]
    public void NothingIsEmittedWithoutAModule() {
        var result = TemplateGeneration.Generate("""
            namespace TestApp;

            public class Person {
                public string Name { get; set; } = "";
            }
            """,
            ("Orphan.html", "{{model TestApp.Person}}{{Name}}"));

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// The generated template class lands in a <c>Generated</c> namespace under the module's, so a
    /// template named after an application type cannot collide with it.
    /// </summary>
    [Fact]
    public void TemplateClassesLiveInAGeneratedNamespaceUnderTheModule() {
        var template = TemplateGeneration
            .Generate(("Person.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateClass("Person");

        Assert.Contains("namespace TestApp.Generated", template);
        Assert.Contains("public class Template_Person", template);
    }

    /// <summary>Each template class implements the handler interface the provider hands back.</summary>
    [Fact]
    public void ATemplateClassImplementsTheExecutionHandlerInterface() {
        var template = TemplateGeneration
            .Generate(("Handler.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateClass("Handler");

        Assert.Contains("ITemplateExecutionHandler", template);
    }
}
