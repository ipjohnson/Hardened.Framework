using System.Linq;
using Hardened.SourceGenerator.Web.Routing;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The constraint fact table: which names compile, what each compiles to, and how they order.
/// </summary>
/// <remarks>
/// <para>
/// <c>Hardened.Web.SourceGenerator.Tests</c> covers the same table through a compiled route table,
/// which is the test worth having — it proves the emitted call is real. It does not cover
/// <em>this</em> copy of the file. <c>RouteConstraintFacts</c> is source-linked into several
/// generator assemblies, and coverage is measured per assembly, so the copy compiled into
/// <c>Hardened.SourceGenerator</c> was 0 of 36 lines however thoroughly the other copy was driven.
/// </para>
/// <para>
/// That is why this file is thin and direct rather than another routing suite: it exists to
/// exercise the table where it is compiled, not to restate what the generator tests already prove.
/// </para>
/// </remarks>
public class RouteConstraintFactsTests {

    [Theory]
    [InlineData("int", "IsInt")]
    [InlineData("long", "IsLong")]
    [InlineData("guid", "IsGuid")]
    [InlineData("bool", "IsBool")]
    [InlineData("decimal", "IsDecimal")]
    [InlineData("date", "IsDate")]
    [InlineData("datetime", "IsDateTime")]
    [InlineData("alpha", "IsAlpha")]
    [InlineData("slug", "IsSlug")]
    [InlineData("hex", "IsHex")]
    public void EveryBuiltInCompilesToItsRuntimeTest(string constraint, string method) {
        var test = RouteConstraintFacts.Test(constraint);

        Assert.NotNull(test);

        // Fully qualified, because generated code carries no usings.
        Assert.Equal("global::Hardened.Web.Runtime.Routing.RouteConstraints." + method, test);
    }

    [Fact]
    public void AnUndeclaredNameCompilesToNothing() {
        Assert.Null(RouteConstraintFacts.Test("isbn"));
    }

    /// <summary>
    /// The rank table is the specification — it decides which handler a request reaches once
    /// alternatives exist at one token position — so every arm is pinned rather than sampled.
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
    /// A name nothing declares sorts after every built-in — the answer that cannot make an existing
    /// route unreachable when an application adds a <c>[RouteConstraint]</c> of its own.
    /// </summary>
    [Fact]
    public void AnUndeclaredNameRanksAsCustom() {
        Assert.Equal(RouteConstraintFacts.CustomPrecedence, RouteConstraintFacts.Rank("isbn"));
        Assert.Equal(90, RouteConstraintFacts.CustomPrecedence);
    }

    /// <summary>
    /// Every name the table compiles is ranked, and every name it lists compiles. A name added to
    /// one and forgotten in the other would sort as a custom constraint or vanish from the
    /// diagnostic — a routing decision made by omission either way.
    /// </summary>
    [Fact]
    public void NamesTestAndRankAgree() {
        foreach (var name in RouteConstraintFacts.Names) {
            Assert.NotNull(RouteConstraintFacts.Test(name));

            Assert.True(
                RouteConstraintFacts.Rank(name) < RouteConstraintFacts.CustomPrecedence,
                $"'{name}' is built in but ranks as a custom constraint.");
        }
    }

    /// <summary>Alphabetical, because this list is read by a person in an error message.</summary>
    [Fact]
    public void NamesAreListedInOrder() {
        Assert.Equal(RouteConstraintFacts.Names.OrderBy(name => name, System.StringComparer.Ordinal),
            RouteConstraintFacts.Names);
    }
}
