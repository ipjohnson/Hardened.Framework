namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// <c>[TemplateHelper]</c> discovery: how a decorated class becomes a token a template can call.
///
/// <para>
/// Helpers are the extension point templates reach user code through, and none of it is written by
/// hand — the generator produces the provider, the token switch and the DI registrations. See
/// <c>docs/framework/templates/helpers.md</c>.
/// </para>
/// </summary>
public class TemplateHelperGeneratorTests {

    private const string Module = """
        using System;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Templates.Abstract;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Person {
            public string Name { get; set; } = "";
        }
        """;

    private static string Helper(string token, string typeName, string? lifecycle = null) => $$"""

        [TemplateHelper("{{token}}"{{(lifecycle == null ? "" : ", TemplateHelperLifecycle." + lifecycle)}})]
        public class {{typeName}} : ITemplateHelper {
            public ValueTask<object> Execute(ITemplateExecutionContext context, params object[] arguments) =>
                new ValueTask<object>("");
        }
        """;

    /// <summary>
    /// The token in the attribute is the token templates use, and it is what the generated switch
    /// matches on. A helper whose token did not survive is unreachable.
    /// </summary>
    [Fact]
    public void TheAttributeTokenBecomesTheLookupCase() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("case \"shout\":", helpers);
    }

    /// <summary>
    /// A helper is resolved from the container per call rather than constructed by the provider, so
    /// its own constructor dependencies are injected normally.
    /// </summary>
    [Fact]
    public void AHelperIsResolvedFromTheContainer() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("provider.GetRequiredService<ShoutHelper>()", helpers);
    }

    /// <summary>
    /// The helper type itself is registered, not just the provider. Without that the
    /// <c>GetRequiredService</c> above throws on the first render that uses the token.
    /// </summary>
    [Fact]
    public void TheHelperTypeIsRegisteredInTheContainer() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("global::TestApp.ShoutHelper", helpers);
    }

    /// <summary>The provider itself is registered so the helper service can find it.</summary>
    [Fact]
    public void TheHelperProviderIsRegisteredAsASingleton() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("ITemplateHelperProvider", helpers);
        Assert.Contains("TestApplication.TemplateHelperProvider", helpers);
    }

    /// <summary>As with templates, the registration must survive trimming for AOT.</summary>
    [Fact]
    public void TheHelperRegistrationMethodIsKeptAliveForTrimming() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("[DynamicDependency(nameof(HardenedTemplateHelperDI))]", helpers);
    }

    /// <summary>Several helpers in one project each get their own case and registration.</summary>
    [Fact]
    public void EveryHelperGetsItsOwnLookupCase() {
        var helpers = TemplateGeneration
            .Generate(
                Module + Helper("shout", "ShoutHelper") + Helper("whisper", "WhisperHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}{{$whisper Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("case \"shout\":", helpers);
        Assert.Contains("case \"whisper\":", helpers);
        Assert.Contains("GetRequiredService<ShoutHelper>()", helpers);
        Assert.Contains("GetRequiredService<WhisperHelper>()", helpers);
    }

    /// <summary>
    /// The factory is cached per token, so repeated renders do not re-resolve the delegate. It still
    /// calls into the container on every invocation, which is what keeps a non-singleton helper's
    /// lifecycle intact.
    /// </summary>
    [Fact]
    public void AHelperFactoryIsCachedPerToken() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("_shoutField ??=", helpers);
    }

    /// <summary>
    /// A project with no helpers gets no helper partial at all. Emitting an empty provider would put
    /// an empty <c>switch</c> in the consumer's build, which raises CS1522 — and CI escalates
    /// warnings to errors.
    /// </summary>
    [Fact]
    public void NoHelperPartialIsEmittedWhenThereAreNoHelpers() {
        var result = TemplateGeneration
            .Generate(Module, ("Page.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors();

        Assert.DoesNotContain("TestApplication.TemplateHelpers.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A helper is discovered from its attribute, not from being used. A helper declared but not yet
    /// called still registers, which is what lets one assembly declare helpers another's templates
    /// call.
    /// </summary>
    [Fact]
    public void AnUnusedHelperIsStillDiscoveredAndRegistered() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("unused", "UnusedHelper"),
                ("Page.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("case \"unused\":", helpers);
    }

    /// <summary>Helpers are discovered with no template files in the project at all.</summary>
    [Fact]
    public void HelpersAreDiscoveredWithNoTemplatesPresent() {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("case \"shout\":", helpers);
    }

    /// <summary>
    /// The template and helper partials both extend the application class and must not collide —
    /// two generated files claiming one hint name silently overwrite each other.
    /// </summary>
    [Fact]
    public void TheTemplateAndHelperPartialsCoexistOnTheApplicationClass() {
        var result = TemplateGeneration
            .Generate(Module + Helper("shout", "ShoutHelper"),
                ("Page.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors();

        Assert.Contains("TestApplication.Templates.cs", result.GeneratedSources.Keys);
        Assert.Contains("TestApplication.TemplateHelpers.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// A helper declaring a lifecycle compiles and is discovered. What it is <em>registered</em> as
    /// is a separate matter — see the note on <c>[TemplateHelper]</c> lifecycles in the templates
    /// workstream report of 2026-08-11.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Singleton")]
    [InlineData("Scoped")]
    [InlineData("Transient")]
    public void AHelperDeclaringAnyLifecycleIsDiscovered(string? lifecycle) {
        var helpers = TemplateGeneration
            .Generate(Module + Helper("cycle", "CycleHelper", lifecycle),
                ("Page.html", "{{model TestApp.Person}}{{$cycle Name}}"))
            .AssertNoErrors()
            .HelperProvider();

        Assert.Contains("case \"cycle\":", helpers);
        Assert.Contains("GetRequiredService<CycleHelper>()", helpers);
    }
}
