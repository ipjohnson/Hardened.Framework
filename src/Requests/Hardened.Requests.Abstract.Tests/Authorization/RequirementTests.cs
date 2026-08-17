using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// The expression tree an authorization decision is made from.
///
/// <para>
/// Every case here is a security decision rather than a behavioural preference: a requirement that
/// evaluates true when it should not is an authorization bypass, and one that reports the wrong
/// <see cref="Requirement.RequiresContext"/> runs at the wrong point in the pipeline. Both fail
/// silently, so both are pinned.
/// </para>
/// </summary>
public class RequirementTests {

    private static readonly IExecutionContext Context = Substitute.For<IExecutionContext>();

    private static ICallerPrincipal Holding(params string[] grants) =>
        new CallerPrincipal("bearer", grants);

    #region grants

    [Fact]
    public void Grant_IsSatisfiedWhenTheCallerHoldsIt() {
        Assert.True(Requirement.Grant("pets:read").IsSatisfiedBy(Holding("pets:read"), Context));
    }

    [Fact]
    public void Grant_IsNotSatisfiedWhenTheCallerDoesNot() {
        Assert.False(Requirement.Grant("pets:write").IsSatisfiedBy(Holding("pets:read"), Context));
    }

    /// <summary>
    /// An anonymous caller holds nothing, so it satisfies nothing - without any code path that knows
    /// what anonymous means. That is the point of the empty grant set.
    /// </summary>
    [Fact]
    public void Grant_IsNotSatisfiedByTheAnonymousPrincipal() {
        Assert.False(
            Requirement.Grant("pets:read")
                .IsSatisfiedBy(AnonymousCallerPrincipal.Instance, Context));
    }

    /// <summary>
    /// Ordinal. <c>pets:read</c> and <c>Pets:Read</c> are different scopes to every authorization
    /// server, and a case-insensitive match here would admit a grant the issuer never made.
    /// </summary>
    [Fact]
    public void Grant_MatchesCaseSensitively() {
        Assert.False(Requirement.Grant("pets:read").IsSatisfiedBy(Holding("Pets:Read"), Context));
    }

    /// <summary>
    /// <c>admin:*</c> is a string in v1, not a pattern. Wildcard semantics invite a grant lattice,
    /// which then constrains what a grant is allowed to be; this pins that the decision was to wait.
    /// </summary>
    [Fact]
    public void Grant_TreatsAWildcardAsAnOrdinaryString() {
        var requirement = Requirement.Grant("admin:*");

        Assert.True(requirement.IsSatisfiedBy(Holding("admin:*"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("admin:delete"), Context));
    }

    [Fact]
    public void Grant_RejectsAnEmptyName() {
        Assert.Throws<ArgumentException>(() => Requirement.Grant(""));
    }

    #endregion

    #region composition

    [Fact]
    public void AllOf_NeedsEveryGrant() {
        var requirement = Requirement.AllOf(
            Requirement.Grant("pets:read"),
            Requirement.Grant("pets:write"));

        Assert.True(requirement.IsSatisfiedBy(Holding("pets:read", "pets:write"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:write"), Context));
    }

    [Fact]
    public void AnyOf_NeedsOnlyOne() {
        var requirement = Requirement.AnyOf(
            Requirement.Grant("pets:read"),
            Requirement.Grant("admin:*"));

        Assert.True(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
        Assert.True(requirement.IsSatisfiedBy(Holding("admin:*"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:write"), Context));
    }

    /// <summary>
    /// The plan's worked example, evaluated. <c>&amp;</c> binds tighter than <c>|</c> in C#, so this
    /// reads as "(read and write) or admin" without the parentheses - but getting that backwards
    /// would turn an AND into an OR silently, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void Operators_BindAndTighterThanOr() {
        var requirement = Requirement.Grant("pets:read") & Requirement.Grant("pets:write")
            | Requirement.Grant("admin:*");

        Assert.True(requirement.IsSatisfiedBy(Holding("pets:read", "pets:write"), Context));
        Assert.True(requirement.IsSatisfiedBy(Holding("admin:*"), Context));

        // The half-satisfied AND branch must not pass on its own.
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:write"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding(), Context));
    }

    [Fact]
    public void Operators_ProduceTheSameResultAsTheNamedCombinators() {
        var written = Requirement.Grant("a") & Requirement.Grant("b");
        var named = Requirement.AllOf(Requirement.Grant("a"), Requirement.Grant("b"));

        Assert.Equal(named.ToString(), written.ToString());
    }

    /// <summary>
    /// Nesting the same kind flattens, so a chain of <c>&amp;</c> is one node rather than a spine.
    /// Changes no result; keeps the rendered form and <see cref="Requirement.RequiredGrants"/>
    /// readable, which both end up in a caller-visible challenge.
    /// </summary>
    [Fact]
    public void Combining_FlattensNestedNodesOfTheSameKind() {
        var requirement = Requirement.Grant("a") & Requirement.Grant("b") & Requirement.Grant("c");

        Assert.Equal("(a & b & c)", requirement.ToString());
    }

    [Fact]
    public void Combining_DoesNotFlattenAcrossKinds() {
        var requirement = (Requirement.Grant("a") & Requirement.Grant("b")) | Requirement.Grant("c");

        Assert.Equal("((a & b) | c)", requirement.ToString());
    }

    [Fact]
    public void Combining_OneRequirementReturnsItUnwrapped() {
        var grant = Requirement.Grant("only");

        Assert.Same(grant, Requirement.AllOf(grant));
        Assert.Same(grant, Requirement.AnyOf(grant));
    }

    /// <summary>
    /// The security-relevant default. An empty set has two readings - "nothing is required" and
    /// "nothing can satisfy this" - and the first silently grants access. A requirement only exists
    /// because something declared a constraint, so an empty one is a bug worth saying out loud.
    /// </summary>
    [Fact]
    public void Combining_NothingThrowsRatherThanEvaluatingToAnything() {
        Assert.Throws<ArgumentException>(() => Requirement.AllOf());
        Assert.Throws<ArgumentException>(() => Requirement.AnyOf());
    }

    #endregion

    #region required grants

    [Fact]
    public void RequiredGrants_NamesEveryGrantInAnAnd() {
        var requirement = Requirement.Grant("pets:read") & Requirement.Grant("pets:write");

        Assert.Equal(["pets:read", "pets:write"], requirement.RequiredGrants);
    }

    /// <summary>
    /// An OR reports every branch rather than picking one. A 403 names what would have satisfied it,
    /// and naming one arbitrary branch would send the caller after the wrong grant.
    /// </summary>
    [Fact]
    public void RequiredGrants_NamesEveryBranchOfAnOr() {
        var requirement = Requirement.Grant("pets:read") | Requirement.Grant("admin:*");

        Assert.Equal(["pets:read", "admin:*"], requirement.RequiredGrants);
    }

    [Fact]
    public void RequiredGrants_DoesNotRepeatAGrantNamedTwice() {
        var requirement = (Requirement.Grant("a") & Requirement.Grant("b"))
            | (Requirement.Grant("a") & Requirement.Grant("c"));

        Assert.Equal(["a", "b", "c"], requirement.RequiredGrants);
    }

    #endregion

    #region pipeline position

    /// <summary>
    /// A requirement over grants alone is decided before the body is read. Reporting true here would
    /// move every handler to the later slot and deserialize bodies for requests that were going to
    /// be rejected anyway.
    /// </summary>
    [Fact]
    public void RequiresContext_IsFalseForGrantsAlone() {
        Assert.False((Requirement.Grant("a") & Requirement.Grant("b")).RequiresContext);
    }

    [Fact]
    public void RequiresContext_IsTrueForAPredicate() {
        Assert.True(Requirement.Predicate((_, _) => true).RequiresContext);
    }

    /// <summary>
    /// One predicate anywhere in the tree moves the whole requirement late. Reporting false because
    /// most of the tree is grants would run the predicate before parameters are bound, which is the
    /// case it exists to serve.
    /// </summary>
    [Fact]
    public void RequiresContext_IsTrueWhenAnyBranchNeedsIt() {
        var requirement = Requirement.Grant("pets:read") | Requirement.Predicate((_, _) => true);

        Assert.True(requirement.RequiresContext);
    }

    #endregion

    #region predicates

    [Fact]
    public void Predicate_IsHandedThePrincipalAndTheContext() {
        ICallerPrincipal? seenPrincipal = null;
        IExecutionContext? seenContext = null;

        var requirement = Requirement.Predicate((principal, context) => {
            seenPrincipal = principal;
            seenContext = context;
            return true;
        });

        var caller = Holding("pets:read");

        Assert.True(requirement.IsSatisfiedBy(caller, Context));
        Assert.Same(caller, seenPrincipal);
        Assert.Same(Context, seenContext);
    }

    [Fact]
    public void Predicate_ContributesNoRequiredGrants() {
        Assert.Empty(Requirement.Predicate((_, _) => true).RequiredGrants);
    }

    /// <summary>
    /// A lambda has no useful name, so the description is what a diagnostic or a log line can show.
    /// </summary>
    [Fact]
    public void Predicate_RendersItsDescription() {
        Assert.Equal(
            "caller owns the pet",
            Requirement.Predicate((_, _) => true, "caller owns the pet").ToString());
    }

    [Fact]
    public void Predicate_RejectsANullDelegate() {
        Assert.Throws<ArgumentNullException>(() => Requirement.Predicate(null!));
    }

    #endregion
}
