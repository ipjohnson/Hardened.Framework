using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web.Routing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// <c>{id:int}</c> and the rest of the constraint vocabulary, driven through the compiled table.
/// </summary>
/// <remarks>
/// <para>
/// A constraint is a guarantee, not a selector. <c>/users/{id}</c> with an <c>int id</c> answers
/// <c>/users/abc</c> with 400 - the route matched and the binder failed. <c>{id:int}</c> makes it a
/// 404, which is the truthful answer: there is no resource at that URL, and 400 implies you
/// addressed a real endpoint incorrectly.
/// </para>
/// <para>
/// Before this, <c>{id:int}</c> was not merely ignored: the whole brace body became the token name,
/// so it matched <c>/constrained/abc</c> and bound nothing to <c>id</c>.
/// </para>
/// </remarks>
public class RouteConstraintTests {

    private static GeneratedRoutingTable Routing(string route) =>
        GeneratedRoutingTable.For($$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ItemController {
                [Get("{{route}}")]
                public string Item(string id) => id;
            }
            """);

    [Theory]
    [InlineData("int", "42", "abc")]
    [InlineData("int", "-7", "4.5")]
    [InlineData("long", "9007199254740993", "abc")]
    [InlineData("guid", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "not-a-guid")]
    [InlineData("bool", "true", "yes")]
    [InlineData("decimal", "4.5", "abc")]
    [InlineData("date", "2026-08-17", "2026-8-17")]
    [InlineData("datetime", "2026-08-17T09:30:00Z", "2026-08-17 09:30")]
    [InlineData("alpha", "beta", "beta2")]
    [InlineData("hex", "0f9AC3", "0g9")]
    [InlineData("slug", "my-first-post", "My-First-Post")]
    public void AConstrainedTokenMatchesOnlyWhatPasses(string constraint, string passes, string fails) {
        var routing = Routing("/items/{id:" + constraint + "}");

        Assert.Equal("Item", routing.Handler("GET", "/items/" + passes).InvokeMethod);
        Assert.Null(routing.Route("GET", "/items/" + fails));
    }

    /// <summary>
    /// A slug is a canonical form. Admitting a leading, trailing or doubled hyphen - or upper case -
    /// would make several URLs for one resource, which is the thing a slug exists to avoid.
    /// </summary>
    [Theory]
    [InlineData("my-first-post", true)]
    [InlineData("post", true)]
    [InlineData("2026-recap", true)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("Upper", false)]
    [InlineData("under_score", false)]
    public void SlugIsACanonicalForm(string segment, bool matches) {
        var routing = Routing("/posts/{id:slug}");

        Assert.Equal(matches, routing.Route("GET", "/posts/" + segment) != null);
    }

    /// <summary>
    /// ISO 8601 only, never <c>DateTime.TryParse</c>. A URL is the same string in every locale, so a
    /// route that accepted <c>12/06/2026</c> would be agreeing to a value whose meaning depends on
    /// where the process happens to be running - and under selection that decides which handler runs.
    /// </summary>
    [Theory]
    [InlineData("2026-08-17", true)]
    [InlineData("12/06/2026", false)]
    [InlineData("17 August 2026", false)]
    [InlineData("2026-13-01", false)]
    public void ADateIsIso8601AndNothingElse(string segment, bool matches) {
        var routing = Routing("/on/{id:date}");

        Assert.Equal(matches, routing.Route("GET", "/on/" + segment) != null);
    }

    /// <summary>
    /// A 404, not a 405. There is no resource at that URL, so reporting which verbs the path
    /// answers would be describing a resource that does not exist.
    /// </summary>
    [Fact]
    public void AValueThatFailsTheConstraintIsNoMatchAtAll() {
        Assert.Null(Routing("/items/{id:int}").Route("GET", "/items/abc"));
    }

    /// <summary>
    /// The constraint is stripped from the name, so the handler's parameter still binds. Leaving it
    /// on is what <c>{id:int}</c> used to do - a token called <c>id:int</c> that bound to nothing.
    /// </summary>
    [Fact]
    public void TheTokenStillBindsUnderItsOwnName() {
        var tokens = Routing("/items/{id:int}").PathTokens("GET", "/items/42");

        Assert.Equal("42", Assert.Contains("id", tokens));
    }

    /// <summary>
    /// A constraint in the middle of a route gates the scan rather than being checked after it, so
    /// a value that fails leaves the scan free to try the next boundary.
    /// </summary>
    [Fact]
    public void AConstraintOnATokenFollowedByMoreRouteAlsoApplies() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders/{id:int}/lines")]
                public string Lines(string id) => id;
            }
            """);

        Assert.Equal("Lines", routing.Handler("GET", "/orders/42/lines").InvokeMethod);
        Assert.Equal("42", Assert.Contains("id", routing.PathTokens("GET", "/orders/42/lines")));
        Assert.Null(routing.Route("GET", "/orders/abc/lines"));
    }

    /// <summary>
    /// Culture-independent. A route is part of a URL, which is the same string in every locale -
    /// parsing under an ambient culture would make the same request match on one machine and not
    /// another.
    /// </summary>
    [Fact]
    public void AConstraintDoesNotDependOnTheAmbientCulture() {
        var routing = Routing("/items/{id:int}");

        // Group separators and a comma decimal point are what a culture-sensitive parse would let
        // through.
        Assert.Null(routing.Route("GET", "/items/1,000"));
        Assert.Null(routing.Route("GET", "/items/1.5"));
    }

    private const string DiagnosticId = "HRDR002";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
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
                        public string Item(string id) => id;
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    /// <summary>
    /// A constraint nothing declares is a build error. Ignoring it would put the route back where
    /// <c>{id:int}</c> started: written, compiled, and constraining nothing.
    /// </summary>
    [Fact]
    public void AnUnknownConstraintIsAnError() {
        var reported = Generate("/items/{id:isbn}").GeneratorDiagnostics
            .SingleOrDefault(diagnostic => diagnostic.Id == DiagnosticId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("isbn", reported.GetMessage());
    }

    /// <summary>And the message lists what is built in, so the fix does not need the docs.</summary>
    [Fact]
    public void TheMessageListsTheBuiltInConstraints() {
        var message = Generate("/items/{id:isbn}").GeneratorDiagnostics
            .Single(diagnostic => diagnostic.Id == DiagnosticId).GetMessage();

        Assert.Contains("int", message);
        Assert.Contains("guid", message);
        Assert.Contains("RouteConstraint", message);
    }

    /// <summary>
    /// Every name the table compiles has a rank, and nothing else does. A name added to
    /// <c>Test</c> and forgotten in <c>Rank</c> would silently sort as a custom constraint - after
    /// every built-in - which is a routing decision made by omission.
    /// </summary>
    [Fact]
    public void EveryBuiltInNameIsRanked() {
        foreach (var name in RouteConstraintFacts.Names) {
            Assert.NotNull(RouteConstraintFacts.Test(name));

            Assert.True(
                RouteConstraintFacts.Rank(name) < RouteConstraintFacts.CustomPrecedence,
                $"'{name}' is built in but ranks as a custom constraint.");
        }
    }

    /// <summary>
    /// Every arm of the rank table, so a name whose rank was never asserted cannot drift. The
    /// numbers are the specification — they decide which handler a request reaches once alternatives
    /// exist — so they are pinned rather than sampled.
    /// </summary>
    [Theory]
    [InlineData("guid", 10)]
    [InlineData("date", 15)]
    [InlineData("datetime", 15)]
    [InlineData("bool", 20)]
    [InlineData("int", 30)]
    [InlineData("min", 32)]
    [InlineData("max", 32)]
    [InlineData("range", 32)]
    [InlineData("long", 35)]
    [InlineData("decimal", 40)]
    [InlineData("hex", 50)]
    [InlineData("alpha", 60)]
    [InlineData("slug", 70)]
    [InlineData("length", 80)]
    [InlineData("minlength", 80)]
    [InlineData("maxlength", 80)]
    public void TheRankTableIsWhatItSays(string constraint, int rank) {
        Assert.Equal(rank, RouteConstraintFacts.Rank(constraint));
    }

    /// <summary>
    /// The ordering that matters is between pairs a route can actually put at one position.
    /// </summary>
    [Theory]
    [InlineData("guid", "int")]
    [InlineData("date", "slug")]
    [InlineData("int", "long")]
    [InlineData("min", "long")]
    [InlineData("hex", "alpha")]
    [InlineData("alpha", "slug")]
    [InlineData("slug", "length")]
    [InlineData("length", "isbn")]
    public void TheNarrowerConstraintRanksFirst(string narrower, string wider) {
        Assert.True(
            RouteConstraintFacts.Rank(narrower) < RouteConstraintFacts.Rank(wider),
            $"'{narrower}' should sort before '{wider}'.");
    }

    /// <summary>An undeclared name gets the custom precedence rather than throwing.</summary>
    [Fact]
    public void AnUnknownNameRanksAsCustom() {
        Assert.Equal(RouteConstraintFacts.CustomPrecedence, RouteConstraintFacts.Rank("isbn"));
    }

    [Theory]
    [InlineData("/items/{id:int}")]
    [InlineData("/items/{id:guid}")]
    [InlineData("/items/{id:slug}")]
    [InlineData("/items/{id:datetime}")]
    [InlineData("/items/{id}")]
    public void ASupportedTokenIsNotReported(string route) {
        Assert.DoesNotContain(
            Generate(route).GeneratorDiagnostics,
            diagnostic => diagnostic.Id == DiagnosticId);
    }

    /// <summary>
    /// The other two forms are still errors, and 1.6 landing does not quietly re-admit them.
    /// </summary>
    [Theory]
    [InlineData("/items/{id?}")]
    [InlineData("/items/{id=5}")]
    public void OptionalAndDefaultAreStillErrors(string route) {
        Assert.Contains(
            Generate(route).GeneratorDiagnostics,
            diagnostic => diagnostic.Id == DiagnosticId);
    }
}
