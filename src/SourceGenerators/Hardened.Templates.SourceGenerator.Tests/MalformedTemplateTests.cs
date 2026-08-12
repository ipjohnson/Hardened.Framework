using Hardened.SourceGeneration.Testing;

namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// What a broken template does to the build.
///
/// <para>
/// Templates are compiled, so a malformed one is a build-time problem, not a run-time one — the
/// selling point in <c>docs/framework/templates/overview.md</c> is "build-time error detection".
/// These cases pin down which malformations are actually detected and what the developer is told,
/// because the failure mode that matters is a template that is quietly dropped and only surfaces as
/// a missing-template exception on the first request that needs it.
/// </para>
///
/// <para>
/// Parse failures escape the incremental pipeline rather than being reported as diagnostics, so they
/// arrive as generator exceptions. They are asserted on directly here; nothing calls
/// <c>AssertNoErrors</c>, because a clean run would mean the malformation went unnoticed.
/// </para>
/// </summary>
public class MalformedTemplateTests {

    /// <summary>A closing tag that does not match the block it closes stops the build.</summary>
    [Fact]
    public void AMismatchedClosingTagNamesBothTags() {
        var result = TemplateGeneration
            .Generate(("Mismatch.html", "{{model TestApp.Person}}{{#each Tags}}x{{/if}}"));

        Assert.Contains(result.GeneratorExceptions,
            exception => exception.Message.Contains("expected 'each'") &&
                         exception.Message.Contains("found 'if'"));
    }

    /// <summary>A closing tag with nothing open stops the build and names the tag.</summary>
    [Fact]
    public void AClosingTagWithNothingOpenNamesTheTag() {
        var result = TemplateGeneration
            .Generate(("Orphan.html", "{{model TestApp.Person}}x{{/each}}"));

        Assert.Contains(result.GeneratorExceptions,
            exception => exception.Message.Contains("No open tag for each"));
    }

    /// <summary>
    /// A token that is opened and never closed. The message quotes the offending text so the
    /// developer can find it — a template file has no line numbers in the compiler output.
    /// </summary>
    [Fact]
    public void AnUnterminatedTokenQuotesTheOffendingText() {
        var result = TemplateGeneration
            .Generate(("Unterminated.html", "{{model TestApp.Person}}<p>{{Name"));

        Assert.Contains(result.GeneratorExceptions,
            exception => exception.Message.Contains("Could not find end of mustache token") &&
                         exception.Message.Contains("{{Name"));
    }

    /// <summary>A quoted helper argument that is never closed.</summary>
    [Fact]
    public void AnUnterminatedStringArgumentIsReported() {
        var result = TemplateGeneration
            .Generate(("BadArg.html", "{{model TestApp.Person}}{{$String.Append Name \"tail}}"));

        Assert.Contains(result.GeneratorExceptions,
            exception => exception.Message.Contains("Could not find end"));
    }

    /// <summary>
    /// A malformed template stops that template from being emitted rather than emitting something
    /// half-formed that fails later with a worse message.
    /// </summary>
    [Fact]
    public void AMalformedTemplateEmitsNoTemplateClass() {
        var result = TemplateGeneration
            .Generate(("Broken.html", "{{model TestApp.Person}}{{#each Tags}}x{{/if}}"));

        Assert.DoesNotContain("Generated.Template_Broken.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// One broken template takes every other template in the project with it.
    ///
    /// <para>
    /// Parsing happens in the incremental pipeline's transform rather than inside the wrapper that
    /// turns failures into diagnostics, so the exception aborts the whole generator run instead of
    /// dropping the single file that caused it. The build fails either way — which is the promise in
    /// <c>docs/framework/templates/overview.md</c> — but the developer sees one message about one
    /// file and every other template silently missing, so a second, unrelated template error stays
    /// hidden until the first is fixed. Recorded 2026-08-11 as the current blast radius; a change
    /// here should be deliberate.
    /// </para>
    /// </summary>
    [Fact]
    public void OneBrokenTemplateSuppressesEveryOtherTemplateInTheProject() {
        var result = TemplateGeneration
            .Generate(
                ("Broken.html", "{{model TestApp.Person}}{{#each Tags}}x{{/if}}"),
                ("Good.html", "{{model TestApp.Person}}<p>{{Name}}</p>"));

        Assert.NotEmpty(result.GeneratorExceptions);
        Assert.DoesNotContain("Generated.Template_Good.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// <c>{{#each}}</c> takes exactly one collection. A second argument is reported rather than
    /// silently ignored, which would iterate the wrong thing.
    /// </summary>
    [Fact]
    public void AnEachBlockWithTwoArgumentsIsReported() {
        var result = TemplateGeneration
            .Generate(("TwoArgs.html", "{{model TestApp.Person}}{{#each Tags Words}}x{{/each}}"));

        AssertReported(result, "each block doesn't support multiple arguments");
    }

    /// <summary>
    /// A template naming a property the model does not have fails in the consuming compiler rather
    /// than in the generator: the generated code reads <c>model.NotAProperty</c> and the C# compiler
    /// rejects it. The build still breaks — which is the documented promise — but the error points
    /// at generated code, not at the template line that caused it.
    /// </summary>
    [Fact]
    public void AMissingPropertyBreaksTheBuildInTheGeneratedCode() {
        var result = TemplateGeneration
            .Generate(("Missing.html", "{{model TestApp.Person}}<p>{{NotAProperty}}</p>"));

        var error = Assert.Single(result.Errors, diagnostic => diagnostic.Id == "CS1061");

        Assert.Contains("NotAProperty", error.GetMessage());
    }

    /// <summary>
    /// A helper token no provider knows is not a build error — helpers are resolved by string at run
    /// time, so an unknown token compiles and throws on first render. Nothing in the build can catch
    /// it, which is worth knowing before relying on a typo being caught.
    /// </summary>
    [Fact]
    public void AnUnknownHelperTokenIsNotABuildError() {
        TemplateGeneration
            .Generate(("Unknown.html", "{{model TestApp.Person}}{{$noSuchHelper Name}}"))
            .AssertNoErrors();
    }

    /// <summary>
    /// A generation-phase failure is reported as a <c>HardenedException</c> diagnostic rather than
    /// crashing the compiler.
    /// </summary>
    private static void AssertReported(GeneratorResult result, string fragment) {
        var reported =
            result.GeneratorDiagnostics.Any(diagnostic => diagnostic.GetMessage().Contains(fragment)) ||
            result.GeneratorExceptions.Any(exception => exception.Message.Contains(fragment));

        Assert.True(reported,
            $"Expected '{fragment}' to be reported. Diagnostics: " +
            string.Join(", ", result.GeneratorDiagnostics.Select(diagnostic => diagnostic.GetMessage())) +
            " Exceptions: " +
            string.Join(", ", result.GeneratorExceptions.Select(exception => exception.Message)));
    }
}
