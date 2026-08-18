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

    /// <summary>
    /// The reverse of <see cref="NamesTestAndRankAgree"/>, and the check that was missing: every
    /// ranked name has to be a name the table can actually compile.
    /// </summary>
    /// <remarks>
    /// Ranks for <c>min</c>, <c>max</c>, <c>range</c> and the <c>length</c> family shipped one
    /// commit before the constraints themselves did, so the table advertised six names that
    /// <c>Call</c> answered null for and <c>{id:min(1)}</c> was still a build error. Checking only
    /// that every name has a rank cannot catch that; this direction can.
    /// </remarks>
    [Theory]
    [InlineData("guid")]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("range")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("hex")]
    [InlineData("alpha")]
    [InlineData("slug")]
    [InlineData("length")]
    [InlineData("minlength")]
    [InlineData("maxlength")]
    public void EveryRankedNameIsANameTheTableCompiles(string name) {
        var arities = RouteConstraintFacts.Arities(name);

        if (arities.Count == 0) {
            Assert.NotNull(RouteConstraintFacts.Test(name));
            return;
        }

        foreach (var arity in arities) {
            var term = new RouteConstraintFacts.Term(name, Enumerable.Repeat(1, arity).ToList());

            Assert.NotNull(RouteConstraintFacts.Call(term));
        }
    }

    [Theory]
    [InlineData("int", 1, 0)]
    [InlineData("int:min(1)", 2, 1)]
    [InlineData("length(6)", 1, 1)]
    [InlineData("length(3,9)", 1, 2)]
    [InlineData("alpha:length(3)", 2, 1)]
    public void TermsParsesAChain(string chain, int terms, int lastArgumentCount) {
        var parsed = RouteConstraintFacts.Terms(chain);

        Assert.NotNull(parsed);
        Assert.Equal(terms, parsed!.Count);
        Assert.Equal(lastArgumentCount, parsed[parsed.Count - 1].Arguments.Count);
    }

    /// <summary>
    /// Malformed text is null rather than a term list, so the caller reports it instead of the
    /// generator emitting a call to something that does not exist.
    /// </summary>
    [Theory]
    [InlineData("length(")]
    [InlineData("length)")]
    [InlineData("length()")]
    [InlineData("length(a)")]
    [InlineData("length(1,)")]
    [InlineData("(6)")]
    [InlineData("int::min(1)")]
    [InlineData("int:")]
    public void TermsRefusesWhatIsNotAChain(string chain) {
        Assert.Null(RouteConstraintFacts.Terms(chain));
    }

    /// <summary>
    /// <c>length</c> is two different tests — <c>length(6)</c> an equality, <c>length(3,9)</c> a
    /// pair of bounds — so arity is part of the lookup, not something checked after it.
    /// </summary>
    [Theory]
    [InlineData("length", 1, "IsLength")]
    [InlineData("length", 2, "IsLength")]
    [InlineData("minlength", 1, "IsMinLength")]
    [InlineData("maxlength", 1, "IsMaxLength")]
    [InlineData("min", 1, "IsMin")]
    [InlineData("max", 1, "IsMax")]
    [InlineData("range", 2, "IsRange")]
    public void AParameterisedNameCompilesAtItsOwnArity(string name, int arity, string method) {
        var term = new RouteConstraintFacts.Term(name, Enumerable.Repeat(1, arity).ToList());

        Assert.Equal("global::Hardened.Web.Runtime.Routing.RouteConstraints." + method,
            RouteConstraintFacts.Call(term));
    }

    [Theory]
    [InlineData("length", 0)]
    [InlineData("length", 3)]
    [InlineData("range", 1)]
    [InlineData("min", 2)]
    [InlineData("int", 1)]
    public void AWrongArityCompilesToNothing(string name, int arity) {
        var term = new RouteConstraintFacts.Term(name, Enumerable.Repeat(1, arity).ToList());

        Assert.Null(RouteConstraintFacts.Call(term));
    }

    /// <summary>Alphabetical, because this list is read by a person in an error message.</summary>
    [Fact]
    public void NamesAreListedInOrder() {
        Assert.Equal(RouteConstraintFacts.Names.OrderBy(name => name, System.StringComparer.Ordinal),
            RouteConstraintFacts.Names);
    }
}
