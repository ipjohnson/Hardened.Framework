namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// The assertion that matters for a generator: the C# it wrote compiles.
///
/// <para>
/// Before 2026-08-11 no test anywhere compiled generated output — suites checked
/// <c>driver.GetRunResult().Diagnostics</c>, which reports what the generator <em>said</em>, not
/// whether what it <em>wrote</em> builds. Three defects shipped that way. This generator had no
/// tests at all, so every case below is new coverage of code a consuming project compiles on every
/// build.
/// </para>
///
/// <para>
/// Each case ends in <see cref="Hardened.SourceGeneration.Testing.GeneratorResult.AssertNoErrors"/>,
/// which compiles the test's own source together with the generated trees.
/// </para>
/// </summary>
public class GeneratedTemplateCompilesTests {

    [Fact]
    public void APlainTemplateCompiles() {
        TemplateGeneration
            .Generate(("Greeting.html", "{{model TestApp.Person}}<h1>Hello {{Name}}</h1>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// A template with no tokens at all. The generator still has to emit a handler class and wire it
    /// into the provider, and the content becomes a verbatim string field.
    /// </summary>
    [Fact]
    public void ATemplateWithNoTokensCompiles() {
        TemplateGeneration
            .Generate(("Static.html", "<p>nothing to substitute</p>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// Content is emitted as a verbatim string literal, so a quote in the markup has to be doubled.
    /// Getting the escaping wrong terminates the literal and the generated file will not parse.
    /// </summary>
    [Fact]
    public void QuotesInTemplateContentCompile() {
        TemplateGeneration
            .Generate(("Quoted.html",
                "{{model TestApp.Person}}<a href=\"/x\" title=\"say &quot;hi&quot;\">{{Name}}</a>"))
            .AssertNoErrors();
    }

    [Fact]
    public void AnEmptyTemplateFileCompiles() {
        TemplateGeneration.Generate(("Empty.html", "")).AssertNoErrors();
    }

    // ---------------------------------------------------------------- model shapes

    /// <summary>
    /// <c>{{model}}</c> emits a cast of the incoming <c>object</c> to the named type, so every shape
    /// a cast is legal for has to produce code that compiles.
    /// </summary>
    [Theory]
    [InlineData("class Shape { public string Name { get; set; } = \"\"; }")]
    [InlineData("record Shape(string Name);")]
    [InlineData("record class Shape(string Name);")]
    [InlineData("struct Shape { public string Name { get; set; } }")]
    [InlineData("record struct Shape(string Name);")]
    [InlineData("interface Shape { string Name { get; } }")]
    [InlineData("abstract class Shape { public abstract string Name { get; } }")]
    [InlineData("sealed class Shape { public string Name { get; set; } = \"\"; }")]
    public void EveryModelShapeCompiles(string declaration) {
        TemplateGeneration
            .Generate($$"""
                namespace TestApp;

                [global::Hardened.Shared.Runtime.Attributes.HardenedModule]
                public partial class TestApplication { }

                public {{declaration}}
                """,
                ("Shaped.html", "{{model TestApp.Shape}}<p>{{Name}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>A model in the global namespace still resolves.</summary>
    [Fact]
    public void AModelInTheGlobalNamespaceCompiles() {
        TemplateGeneration
            .Generate("""
                [global::Hardened.Shared.Runtime.Attributes.HardenedModule]
                public partial class TestApplication { }

                public class RootModel {
                    public string Name { get; set; } = "";
                }
                """,
                ("Root.html", "{{model RootModel}}<p>{{Name}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>A framework type is as valid a model as an application one.</summary>
    [Fact]
    public void ABclTypeAsTheModelCompiles() {
        TemplateGeneration
            .Generate(("Length.html", "{{model System.String}}<p>{{Length}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// A generic model. The cast target carries angle brackets straight into the emitted C#, so the
    /// type name has to survive the round trip intact.
    /// </summary>
    [Fact]
    public void AGenericModelCompiles() {
        TemplateGeneration
            .Generate(("Counted.html",
                "{{model System.Collections.Generic.List<TestApp.Tag>}}<p>{{Count}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// No <c>{{model}}</c> declaration at all. Nothing binds to a model, and the generated
    /// <c>Execute</c> must not reference the <c>model</c> variable it never declared.
    /// </summary>
    [Fact]
    public void ATemplateWithNoModelDeclarationCompiles() {
        TemplateGeneration
            .Generate(("NoModel.html", "<p>no model here</p>"))
            .AssertNoErrors();
    }

    // ---------------------------------------------------------------- extensions

    /// <summary>
    /// The four extensions the generator compiles. Each picks a different escape service at
    /// initialisation, so each emits a different literal into the generated <c>Initialize</c>.
    /// </summary>
    [Theory]
    [InlineData("html")]
    [InlineData("js")]
    [InlineData("css")]
    [InlineData("md")]
    public void EveryCompiledExtensionCompiles(string extension) {
        TemplateGeneration
            .Generate(($"Asset.{extension}", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors();
    }

    /// <summary>All four extensions in one project, side by side, without colliding.</summary>
    [Fact]
    public void AllFourExtensionsInOneProjectCompile() {
        TemplateGeneration
            .Generate(
                ("Page.html", "{{model TestApp.Person}}<h1>{{Name}}</h1>"),
                ("Script.js", "{{model TestApp.Person}}var name = \"{{Name}}\";"),
                ("Sheet.css", "{{model TestApp.Person}}.name::after { content: \"{{Name}}\"; }"),
                ("Readme.md", "{{model TestApp.Person}}# {{Name}}"))
            .AssertNoErrors();
    }

    // ---------------------------------------------------------------- syntax

    /// <summary>
    /// <c>{{using}}</c> adds a namespace import to the generated file, which is what lets
    /// <c>{{model}}</c> name a type without qualifying it.
    /// </summary>
    [Fact]
    public void AUsingDeclarationLetsTheModelBeUnqualified() {
        TemplateGeneration
            .Generate(("Unqualified.html", "{{model Person}}{{using TestApp}}<p>{{Name}}</p>"))
            .AssertNoErrors();
    }

    /// <summary><c>{{inject}}</c> resolves a service from the request provider into a local.</summary>
    [Fact]
    public void AnInjectDeclarationCompiles() {
        TemplateGeneration
            .Generate(("Injected.html",
                "{{model TestApp.Person}}{{inject TestApp.Tag tag}}<p>{{Name}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>A format string after the colon reaches <c>FormatData</c> as its format argument.</summary>
    [Theory]
    [InlineData("Born", "yyyy-MM-dd")]
    [InlineData("Balance", "C")]
    [InlineData("Age", "N0")]
    public void AFormatStringCompiles(string property, string format) {
        TemplateGeneration
            .Generate(("Formatted.html",
                "{{model TestApp.Person}}<p>{{" + property + " : \"" + format + "\"}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>Triple braces write the value without running it through the escape service.</summary>
    [Fact]
    public void ARawTokenCompiles() {
        TemplateGeneration
            .Generate(("Raw.html", "{{model TestApp.Person}}<div>{{{Name}}}</div>"))
            .AssertNoErrors();
    }

    /// <summary>The same property referenced repeatedly must not redeclare anything.</summary>
    [Fact]
    public void RepeatingATokenCompiles() {
        TemplateGeneration
            .Generate(("Repeat.html",
                "{{model TestApp.Person}}<p>{{Name}}</p><p>{{Name}}</p><p>{{Name}}</p>"))
            .AssertNoErrors();
    }

    /// <summary>A nullable property is written through <c>FormatData</c>, which handles null.</summary>
    [Fact]
    public void ANullablePropertyCompiles() {
        TemplateGeneration
            .Generate(("Nullable.html", "{{model TestApp.Person}}<p>{{Nickname}}</p>"))
            .AssertNoErrors();
    }

    // ---------------------------------------------------------------- blocks

    [Fact]
    public void AnEachBlockCompiles() {
        TemplateGeneration
            .Generate(("Each.html",
                "{{model TestApp.Person}}<ul>{{#each Tags}}<li>{{Label}}</li>{{/each}}</ul>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// Two <c>{{#each}}</c> blocks in one template. Each needs its own loop variable — reusing one
    /// name would redeclare it in the same scope.
    /// </summary>
    [Fact]
    public void TwoSiblingEachBlocksGetDistinctLoopVariables() {
        TemplateGeneration
            .Generate(("Siblings.html",
                "{{model TestApp.Person}}{{#each Tags}}<li>{{Label}}</li>{{/each}}" +
                "{{#each Tags}}<li>{{Label}}</li>{{/each}}"))
            .AssertNoErrors();
    }

    /// <summary>A nested loop shadows nothing: the inner block gets its own variable.</summary>
    [Fact]
    public void ANestedEachBlockCompiles() {
        TemplateGeneration
            .Generate(("Nested.html",
                "{{model TestApp.Person}}{{#each Tags}}<b>{{Label}}</b>" +
                "{{#each Aliases}}<i>{{Length}}</i>{{/each}}{{/each}}"))
            .AssertNoErrors();
    }

    [Fact]
    public void AnIfBlockCompiles() {
        TemplateGeneration
            .Generate(("If.html", "{{model TestApp.Person}}{{#if Active}}<p>on</p>{{/if}}"))
            .AssertNoErrors();
    }

    [Fact]
    public void AnIfElseBlockCompiles() {
        TemplateGeneration
            .Generate(("IfElse.html",
                "{{model TestApp.Person}}{{#if Active}}<p>on</p>{{else}}<p>off</p>{{/if}}"))
            .AssertNoErrors();
    }

    [Fact]
    public void AnElseIfChainCompiles() {
        TemplateGeneration
            .Generate(("ElseIf.html",
                "{{model TestApp.Person}}{{#if Active}}a{{else if Age}}b{{else}}c{{/if}}"))
            .AssertNoErrors();
    }

    /// <summary>An <c>if</c> inside an <c>each</c> tests against the loop variable, not the model.</summary>
    [Fact]
    public void AnIfInsideAnEachCompiles() {
        TemplateGeneration
            .Generate(("IfInEach.html",
                "{{model TestApp.Person}}{{#each Tags}}{{#if Label}}<li>{{Label}}</li>{{/if}}{{/each}}"))
            .AssertNoErrors();
    }

    /// <summary>An <c>each</c> inside an <c>if</c>, the other nesting order.</summary>
    [Fact]
    public void AnEachInsideAnIfCompiles() {
        TemplateGeneration
            .Generate(("EachInIf.html",
                "{{model TestApp.Person}}{{#if Active}}{{#each Tags}}<li>{{Label}}</li>{{/each}}{{/if}}"))
            .AssertNoErrors();
    }

    /// <summary>Tilde trim markers change the emitted content fields, not the code structure.</summary>
    [Fact]
    public void TildeTrimMarkersCompile() {
        TemplateGeneration
            .Generate(("Trimmed.html", """
                {{model TestApp.Person}}
                {{~#each Tags~}}
                    <li>{{Label}}</li>
                {{~/each~}}
                """))
            .AssertNoErrors();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Helper tokens with zero, one and several arguments. Each argument arrives as a positional
    /// entry in the <c>params object[]</c> the helper receives, and a helper call forces the
    /// generated <c>Execute</c> to become <c>async</c>.
    /// </summary>
    [Theory]
    [InlineData("{{$String.ToUpper}}")]
    [InlineData("{{$String.ToUpper Name}}")]
    [InlineData("{{$String.Concat Name Nickname}}")]
    [InlineData("{{$String.Replace Name \"a\" \"b\"}}")]
    [InlineData("{{$String.Format \"{0}-{1}\" Name Age}}")]
    public void HelperTokensOfEveryArityCompile(string token) {
        TemplateGeneration
            .Generate(("Helper.html", "{{model TestApp.Person}}<p>" + token + "</p>"))
            .AssertNoErrors();
    }

    /// <summary>A helper whose result is written raw rather than escaped.</summary>
    [Fact]
    public void ARawHelperTokenCompiles() {
        TemplateGeneration
            .Generate(("RawHelper.html", "{{model TestApp.Person}}<div>{{{$String.ToUpper Name}}}</div>"))
            .AssertNoErrors();
    }

    /// <summary>
    /// The same helper used twice shares one backing field, so the second use must not redeclare it.
    /// </summary>
    [Fact]
    public void TheSameHelperUsedTwiceCompiles() {
        TemplateGeneration
            .Generate(("TwiceHelper.html",
                "{{model TestApp.Person}}{{$String.ToUpper Name}}{{$String.ToUpper Nickname}}"))
            .AssertNoErrors();
    }

    /// <summary>A helper called against the loop variable inside an <c>each</c>.</summary>
    [Fact]
    public void AHelperInsideAnEachCompiles() {
        TemplateGeneration
            .Generate(("HelperInEach.html",
                "{{model TestApp.Person}}{{#each Tags}}<li>{{$String.ToUpper Label}}</li>{{/each}}"))
            .AssertNoErrors();
    }

    /// <summary>A helper token whose name contains a dot maps to a field with the dot sanitised.</summary>
    [Fact]
    public void ADottedHelperTokenCompiles() {
        TemplateGeneration
            .Generate(("Dotted.html", "{{model TestApp.Person}}{{$Url.Encode Name}}"))
            .AssertNoErrors();
    }

    /// <summary>A custom <c>[TemplateHelper]</c> is discovered, registered and callable.</summary>
    [Fact]
    public void ACustomHelperCompiles() {
        TemplateGeneration
            .Generate(CustomHelperApplication, ("Custom.html", "{{model TestApp.Person}}{{$shout Name}}"))
            .AssertNoErrors();
    }

    // ---------------------------------------------------------------- whole projects

    /// <summary>
    /// Several templates and a custom helper in one compilation — the shape a real project has. The
    /// application class receives two generated partials, and they must not collide.
    /// </summary>
    [Fact]
    public void AProjectWithTemplatesAndHelpersCompiles() {
        TemplateGeneration
            .Generate(CustomHelperApplication,
                ("Index.html", "{{model TestApp.Person}}<h1>{{$shout Name}}</h1>"),
                ("Detail.html", "{{model TestApp.Person}}{{#each Tags}}<li>{{Label}}</li>{{/each}}"),
                ("App.css", "{{model TestApp.Person}}.n { content: \"{{Name}}\"; }"))
            .AssertNoErrors();
    }

    /// <summary>
    /// A compilation with a module but no template files. The provider is still emitted and must not
    /// contain an empty <c>switch</c>: an empty switch block is legal C# but raises CS1522 in the
    /// consuming project, which makes generated code the source of a warning nobody can suppress.
    /// CI builds with <c>TreatWarningsAsErrors</c>, so that warning is a build break.
    /// </summary>
    [Fact]
    public void AProjectWithNoTemplatesCompilesWithoutWarnings() {
        var result = TemplateGeneration.Generate().AssertNoErrors();

        Assert.DoesNotContain("switch", result.TemplateProvider());
        Assert.Empty(result.CompilationDiagnostics.Where(diagnostic => diagnostic.Id == "CS1522"));
    }

    internal const string CustomHelperApplication = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Templates.Abstract;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Tag {
            public string Label { get; set; } = "";
        }

        public class Person {
            public string Name { get; set; } = "";
            public string? Nickname { get; set; }
            public IEnumerable<Tag> Tags { get; set; } = new List<Tag>();
        }

        [TemplateHelper("shout")]
        public class ShoutHelper : ITemplateHelper {
            public ValueTask<object> Execute(ITemplateExecutionContext context, params object[] arguments) =>
                new ValueTask<object>((arguments.Length > 0 ? arguments[0]?.ToString() ?? "" : "") + "!");
        }
        """;
}
