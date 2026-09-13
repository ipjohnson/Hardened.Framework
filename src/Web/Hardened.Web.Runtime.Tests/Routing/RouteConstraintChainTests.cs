using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// The constraint vocabulary, resolved the way a template registered at run time resolves it.
/// </summary>
/// <remarks>
/// The names, their arities and their precedence numbers are the ones the generator compiles and
/// the routing guide publishes. This file is what holds the two readings of that vocabulary
/// together: a name the generator emits a call for and this refuses would be a route that builds
/// one way and fails to register the other.
/// </remarks>
public class RouteConstraintChainTests
{
    [Theory]
    [InlineData("int", "7", "seven")]
    [InlineData("long", "7", "seven")]
    [InlineData("guid", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "nope")]
    [InlineData("bool", "true", "yes")]
    [InlineData("decimal", "4.5", "four")]
    [InlineData("date", "2026-09-13", "13/09/2026")]
    [InlineData("datetime", "2026-09-13T10:00:00Z", "yesterday")]
    [InlineData("alpha", "abc", "ab1")]
    [InlineData("slug", "blue-hat", "Blue-Hat")]
    [InlineData("hex", "deadbeef", "ghijkl")]
    [InlineData("length(3)", "abc", "abcd")]
    [InlineData("length(2,4)", "abc", "abcde")]
    [InlineData("minlength(3)", "abcd", "ab")]
    [InlineData("maxlength(3)", "ab", "abcd")]
    [InlineData("min(5)", "6", "4")]
    [InlineData("max(5)", "4", "6")]
    [InlineData("range(1,9)", "5", "11")]
    public void EveryBuiltInNameResolvesToItsTest(string chain, string passes, string fails)
    {
        Assert.True(
            RouteConstraintChain.TryResolve(chain, null, out var tests, out _, out var error),
            error
        );
        Assert.True(RouteConstraintChain.Passes(tests, passes));
        Assert.False(RouteConstraintChain.Passes(tests, fails));
    }

    /// <remarks>
    /// Lower is narrower and is tried first. The numbers are declared rather than inferred, and
    /// this is the whole specification of them.
    /// </remarks>
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
    [InlineData("isbn", RouteConstraintChain.CustomPrecedence)]
    public void EveryNameHasItsPublishedPrecedence(string name, int rank) =>
        Assert.Equal(rank, RouteConstraintChain.Rank(name));

    [Fact]
    public void NoChainConstrainsNothing()
    {
        Assert.True(
            RouteConstraintChain.TryResolve(null, null, out var tests, out var rank, out _)
        );
        Assert.Empty(tests);
        Assert.Equal(RouteConstraintChain.UnconstrainedPrecedence, rank);
    }

    /// <remarks>
    /// The narrowest term in the chain decides where the token sorts, because passing the chain
    /// means passing all of it.
    /// </remarks>
    [Fact]
    public void AChainSortsByItsNarrowestTerm()
    {
        Assert.True(RouteConstraintChain.TryResolve("slug:int", null, out _, out var rank, out _));
        Assert.Equal(30, rank);
    }

    [Fact]
    public void EveryTermInAChainHasToPass()
    {
        Assert.True(
            RouteConstraintChain.TryResolve("int:min(5)", null, out var tests, out _, out _)
        );
        Assert.True(RouteConstraintChain.Passes(tests, "6"));
        Assert.False(RouteConstraintChain.Passes(tests, "4"));
        Assert.False(RouteConstraintChain.Passes(tests, "six"));
    }

    [Fact]
    public void ADeclaredConstraintSortsAfterEveryBuiltIn()
    {
        var declared = new Dictionary<string, RouteConstraintTest>
        {
            { "isbn", static value => value.Length == 13 },
        };

        Assert.True(
            RouteConstraintChain.TryResolve("isbn", declared, out var tests, out var rank, out _)
        );
        Assert.Equal(RouteConstraintChain.CustomPrecedence, rank);
        Assert.True(RouteConstraintChain.Passes(tests, "9780306406157"));
    }

    [Theory]
    [InlineData("", "empty term")]
    [InlineData("int:", "empty term")]
    [InlineData("isbn", "nothing declares a route constraint")]
    [InlineData("length)", "')' with no '('")]
    [InlineData("(3)", "not a name followed by its arguments")]
    [InlineData("length(3", "not a name followed by its arguments")]
    [InlineData("length()", "no arguments between its brackets")]
    [InlineData("length(x)", "takes whole numbers")]
    [InlineData("int(3)", "takes no arguments")]
    [InlineData("length(1,2,3)", "does not take 3")]
    [InlineData("isbn(3)", "nothing declares a route constraint")]
    public void AChainThatIsNotOneIsReported(string chain, string expected)
    {
        Assert.False(
            RouteConstraintChain.TryResolve("int:" + chain, null, out _, out _, out var error)
        );
        Assert.Contains(expected, error);
    }
}
