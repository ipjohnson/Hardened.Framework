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
/// <c>{id:int}</c> matched <c>/constrained/abc</c> and bound nothing to <c>id</c>, and
/// <c>{id?}</c> made a segment mandatory under a strange name rather than optional. Both routed,
/// both compiled, and neither did what it was written to do. A route is a contract with every
/// client of the application, so this fails the build.
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
    [InlineData("/items/{id:int}")]
    [InlineData("/items/{id?}")]
    [InlineData("/items/{id=5}")]
    public void AnUnsupportedTokenFormIsAnError(string route) {
        Assert.Equal(DiagnosticSeverity.Error, Reported(route).Severity);
    }

    /// <summary>
    /// The message quotes the token as written. A diagnostic that says only "unsupported syntax"
    /// against a route with four tokens is a search, not a fix.
    /// </summary>
    [Theory]
    [InlineData("/items/{id:int}", "{id:int}")]
    [InlineData("/items/{id?}", "{id?}")]
    [InlineData("/items/{id=5}", "{id=5}")]
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
        var message = Reported("/items/{id:int}").GetMessage();

        Assert.Contains("/items/{id:int}", message);
        Assert.Contains("ItemController", message);
        Assert.Contains("ItemById", message);
    }

    [Theory]
    [InlineData("/items/{id}")]
    [InlineData("/items/{*id}")]
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
        var reported = Generate("/items/{id:int}/parts/{partId?}").GeneratorDiagnostics
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
        var result = Generate("/items/{id:int}");

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("ItemController_ItemById"));
    }
}
