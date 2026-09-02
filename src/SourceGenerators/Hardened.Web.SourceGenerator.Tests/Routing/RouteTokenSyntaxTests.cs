using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Brace forms borrowed from other routing systems, which Hardened does not compile.
///
/// <para>
/// They were not merely ignored — the whole brace body became the token <em>name</em>, so
/// <c>{id?}</c> made a segment mandatory under a strange name rather than optional. It routed, it
/// compiled, and it did not do what it was written to do. A route is a contract with every client
/// of the application, so this fails the build.
/// </para>
///
/// <para>
/// <c>{id:int}</c> was here too until constraints landed. What is left of the colon form is a name
/// nothing declares, which <c>RouteConstraintTests</c> covers.
/// </para>
/// </summary>
public class RouteTokenSyntaxTests {
    private const string DiagnosticId = "HRDR002";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string route) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public class ItemController {
                        [Get("{{route}}")]
                        public string ItemById(string id) => id;
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static Diagnostic Reported(string route) {
        var result = Generate(route);

        var diagnostic = result.GeneratorDiagnostics
            .SingleOrDefault(reported => reported.Id == DiagnosticId);

        Assert.True(diagnostic != null,
            $"'{route}' reported no {DiagnosticId}. Reported: " +
            string.Join(", ", result.GeneratorDiagnostics.Select(reported => reported.Id)));

        return diagnostic!;
    }

    [Theory]
    [InlineData("/items/{id?}")]
    [InlineData("/items/{id=5}")]
    [InlineData("/items/{id")]
    [InlineData("/items/}")]
    [InlineData("/items/{}")]
    [InlineData("/items/{id}/parts/{id}")]
    public void AnUnsupportedTokenFormIsAnError(string route) {
        Assert.Equal(DiagnosticSeverity.Error, Reported(route).Severity);
    }

    /// <summary>
    /// The message quotes the token as written. A diagnostic that says only "unsupported syntax"
    /// against a route with four tokens is a search, not a fix.
    /// </summary>
    [Theory]
    [InlineData("/items/{id?}", "{id?}")]
    [InlineData("/items/{id=5}", "{id=5}")]
    [InlineData("/items/{id", "{id")]
    [InlineData("/items/{}", "{}")]
    public void TheMessageNamesTheTokenAsWritten(string route, string token) {
        Assert.Contains(token, Reported(route).GetMessage());
    }

    /// <summary>
    /// And the route and handler, because the diagnostic carries no source location — a syntax
    /// location on the model would travel through the incremental caches, which compare models
    /// for equality to decide whether to regenerate.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheRouteAndHandler() {
        var message = Reported("/items/{id?}").GetMessage();

        Assert.Contains("/items/{id?}", message);
        Assert.Contains("ItemController", message);
        Assert.Contains("ItemById", message);
    }

    [Theory]
    [InlineData("/items/{id}")]
    [InlineData("/items/{*id}")]
    [InlineData("/items/{id:int}")]
    [InlineData("/items/{id}/parts/{partId}")]
    [InlineData("/items")]
    public void SupportedFormsAreNotReported(string route) {
        Assert.DoesNotContain(
            Generate(route).GeneratorDiagnostics,
            reported => reported.Id == DiagnosticId);
    }

    /// <summary>
    /// One report per bad token, not one per route. Fixing the first should not be how you
    /// discover the second.
    /// </summary>
    [Fact]
    public void EveryUnsupportedTokenInARouteIsReported() {
        var reported = Generate("/items/{id?}/parts/{partId=5}").GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticId)
            .ToArray();

        Assert.Equal(2, reported.Length);
    }

    /// <summary>
    /// The handler is still emitted. The routing table filters on unresolved parameters rather
    /// than on token syntax, so dropping the handler here would leave it routing to a class that
    /// does not exist — burying the one diagnostic that says what is wrong under a pile of
    /// CS0246s.
    /// </summary>
    [Fact]
    public void TheHandlerIsStillEmitted() {
        var result = Generate("/items/{id?}");

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("ItemController_ItemById"));
    }

    #region a template that is not well formed

    /// <summary>
    /// CS-12. <c>[Get("/{eventId")]</c> built with zero warnings: the unclosed token was matched as
    /// literal text, so the route answered nothing anybody sent, and the parameter it was written
    /// to bind was read from the request body instead.
    /// </summary>
    [Fact]
    public void AnUnclosedTokenSaysWhatItCostsAndHowToFixIt() {
        var message = Reported("/items/{id").GetMessage();

        Assert.Contains("no partner", message);
        Assert.Contains("literal text", message);
    }

    /// <summary>
    /// A closing brace with no opening one is the same mistake seen from the other end.
    /// </summary>
    [Fact]
    public void AStrayClosingBraceIsReported() {
        Assert.Equal(DiagnosticSeverity.Error, Reported("/items/}").Severity);
    }

    /// <summary>
    /// A token with no name is not a token. It binds nothing and matches one segment of anything.
    /// </summary>
    [Theory]
    [InlineData("/items/{}")]
    [InlineData("/items/{:int}")]
    public void AnUnnamedTokenIsReported(string route) {
        Assert.Contains("binds nothing", Reported(route).GetMessage());
    }

    /// <summary>
    /// One name declared twice cannot bind one parameter twice, so only one of the two segments a
    /// request sends ever reaches it.
    /// </summary>
    [Fact]
    public void ANameDeclaredTwiceIsReported() {
        var message = Reported("/items/{id}/parts/{id}").GetMessage();

        Assert.Contains("declared twice", message);
        Assert.Contains("distinct names", message);
    }

    /// <summary>
    /// Two tokens that differ are not a duplicate, which is the ordinary shape of a nested route.
    /// </summary>
    [Fact]
    public void TwoDistinctTokensAreNotADuplicate() {
        Assert.DoesNotContain(
            Generate("/items/{id}/parts/{partId}").GeneratorDiagnostics,
            reported => reported.Id == DiagnosticId);
    }

    #endregion
}
