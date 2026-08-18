using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What the generator does with <c>[FromForm]</c>.
/// </summary>
public class FormBindingTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
    ];

    private static GeneratorResult Generate(string handlers) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public class Credentials {
                        public string Username { get; set; } = "";
                    }

                    public class SignInController {
                    {{handlers}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    /// <summary>
    /// The form is read once, whatever the number of fields bound from it.
    /// </summary>
    /// <remarks>
    /// Reading it reads the body, so a second read on a non-seekable stream returns nothing. Doing
    /// it once per handler makes that structural rather than something <c>FormReader</c> has to
    /// cache against a request it is a singleton relative to.
    /// </remarks>
    [Fact]
    public void TheFormIsReadOncePerHandler() {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn(
                    [FromForm] string username, [FromForm] string password, [FromForm] string totp)
                    => username;
            """);

        result.AssertNoErrors();

        var source = result.SourceContaining("SignIn");
        var reads = source.Split("FormReader.ReadForm").Length - 1;

        Assert.Equal(1, reads);
        Assert.Contains("form.Get(\"username\")", source);
        Assert.Contains("form.Get(\"password\")", source);
        Assert.Contains("form.Get(\"totp\")", source);
    }

    /// <summary>A handler with no form parameter never reads one.</summary>
    [Fact]
    public void AHandlerWithNoFormParameterDoesNotReadOne() {
        var result = Generate(
            """
                [Get("/whoami")]
                public string WhoAmI([FromQueryString] string id) => id;
            """);

        result.AssertNoErrors();

        Assert.DoesNotContain("ReadForm", result.SourceContaining("WhoAmI"));
    }

    /// <summary>The wire name comes from the attribute when it carries one.</summary>
    [Fact]
    public void AnAttributeNameOverridesTheParameterName() {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn([FromForm("user_name")] string userName) => userName;
            """);

        result.AssertNoErrors();

        Assert.Contains("form.Get(\"user_name\")", result.SourceContaining("SignIn"));
    }

    /// <summary>
    /// Binding a form and a body on one handler is a build error.
    /// </summary>
    /// <remarks>
    /// There is one body and the two read it differently, so whichever runs second sees a consumed
    /// stream. The failure is otherwise a silently empty model or a silently empty set of fields on
    /// a handler that compiles and routes correctly.
    /// </remarks>
    [Fact]
    public void AFormAndABodyTogetherIsABuildError() {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn([FromForm] string username, Credentials credentials)
                    => username;
            """);

        var reported = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormAndBodyDiagnostics.DiagnosticId);

        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("username", reported.GetMessage());
        Assert.Contains("credentials", reported.GetMessage());
    }

    /// <summary>And a handler with only one of the two is not reported.</summary>
    [Theory]
    [InlineData("[FromForm] string username")]
    [InlineData("Credentials credentials")]
    public void OneOrTheOtherIsFine(string parameter) {
        var result = Generate(
            $$"""
                [Post("/sign-in")]
                public string SignIn({{parameter}}) => "";
            """);

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormAndBodyDiagnostics.DiagnosticId);
    }
}
