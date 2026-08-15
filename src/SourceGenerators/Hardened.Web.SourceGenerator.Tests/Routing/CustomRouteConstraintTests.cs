using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A constraint the application declares itself.
/// </summary>
/// <remarks>
/// A build-time contract, because a constraint has to be known at compile time to be compiled in.
/// The generator emits a direct static call - no allocation, no reflection, no registry, and
/// nothing to look up per request - which is also what makes the failure legible: a name nothing
/// declares is a build error rather than a route that silently constrains nothing.
/// </remarks>
public class CustomRouteConstraintTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
    ];

    /// <summary>A three-letter uppercase code, and a route that uses it.</summary>
    private static string Application(string signature) =>
        $$"""
        using System;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public static class Codes {
            [RouteConstraint("code")]
            {{signature}}
        }

        public class ItemController {
            [Get("/items/{id:code}")]
            public string Item(string id) => id;
        }
        """;

    private const string ValidSignature =
        """
        public static bool IsCode(ReadOnlySpan<char> value) {
                if (value.Length != 3) {
                    return false;
                }

                foreach (var character in value) {
                    if (character < 'A' || character > 'Z') {
                        return false;
                    }
                }

                return true;
            }
        """;

    private static GeneratedRoutingTable Routing() =>
        GeneratedRoutingTable.For(Application(ValidSignature));

    private static GeneratorResult Generate(string signature) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application(signature) },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    /// <summary>
    /// The declared test decides the match, on the same terms as a built-in one: a value that fails
    /// it is no resource at that URL rather than a 400 from a binder.
    /// </summary>
    [Fact]
    public void ADeclaredConstraintDecidesTheMatch() {
        var routing = Routing();

        Assert.Equal("Item", routing.Handler("GET", "/items/ABC").InvokeMethod);
        Assert.Null(routing.Route("GET", "/items/abc"));
        Assert.Null(routing.Route("GET", "/items/ABCD"));
    }

    /// <summary>And the token still binds under its own name.</summary>
    [Fact]
    public void TheTokenStillBinds() {
        Assert.Equal("ABC", Assert.Contains("id", Routing().PathTokens("GET", "/items/ABC")));
    }

    /// <summary>
    /// A direct static call, not a lookup. The whole reason a constraint is a build-time contract
    /// is that there is nothing left to resolve per request.
    /// </summary>
    [Fact]
    public void TheTableCallsTheDeclaredMethodDirectly() {
        Assert.Contains(
            "global::TestApp.Codes.IsCode(",
            Generate(ValidSignature).AssertNoErrors().SourceContaining("Routing"));
    }

    /// <summary>
    /// Declaring one is what stops the name being reported as unknown, which is the diagnostic that
    /// would otherwise make a custom constraint impossible to use.
    /// </summary>
    [Fact]
    public void ADeclaredNameIsNotReportedAsUnknown() {
        Assert.DoesNotContain(
            Generate(ValidSignature).GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "HRDR002");
    }

    /// <summary>
    /// The span is the rule rather than a preference: a constraint runs on every request that
    /// reaches the position it guards, including the ones it rejects, so a string parameter would
    /// allocate to decide that a request does not match.
    /// </summary>
    [Theory]
    [InlineData("public static bool IsCode(string value) => value.Length == 3;")]
    [InlineData("public bool IsCode(ReadOnlySpan<char> value) => value.Length == 3;")]
    [InlineData("public static string IsCode(ReadOnlySpan<char> value) => \"\";")]
    [InlineData("public static bool IsCode(ReadOnlySpan<char> value, int length) => true;")]
    public void AWrongSignatureIsReported(string signature) {
        Assert.Contains(
            Generate(signature).GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "HRDR003");
    }

    /// <summary>
    /// And is not called. Emitting a call to a method that is not a static
    /// <c>bool(ReadOnlySpan&lt;char&gt;)</c> would bury the diagnostic that says so under a
    /// compiler error in generated code.
    /// </summary>
    [Fact]
    public void AWrongSignatureIsNotCalled() {
        var result = Generate("public static bool IsCode(string value) => value.Length == 3;");

        Assert.DoesNotContain("Codes.IsCode(", result.SourceContaining("Routing"));
    }
}
