using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// A named policy, written the way an application would write one.
///
/// <para>
/// The policies below are the plan's own worked examples rather than invented ones, because what is
/// being checked is that the documented shape compiles and evaluates as documented - the combinators
/// have to be reachable unqualified from inside <c>Define</c>, or every policy in every consuming
/// application is more verbose than the guide says.
/// </para>
/// </summary>
public class AuthorizationPolicyTests {

    private static readonly IExecutionContext Context = Substitute.For<IExecutionContext>();

    /// <summary>The plan's example, verbatim.</summary>
    private class CanManagePets : AuthorizationPolicy {
        protected override Requirement Define() =>
            (Grant("pets:read") & Grant("pets:write")) | Grant("admin:*");
    }

    private class CountingPolicy : AuthorizationPolicy {
        public int DefineCalls { get; private set; }

        protected override Requirement Define() {
            DefineCalls++;
            return Grant("counted");
        }
    }

    private class OwnsTheResource : AuthorizationPolicy {
        protected override Requirement Define() =>
            Grant("pets:read") & Predicate((principal, _) => principal.Subject == "owner", "is owner");
    }

    private class UsesNamedCombinators : AuthorizationPolicy {
        protected override Requirement Define() =>
            AnyOf(AllOf(Grant("a"), Grant("b")), Grant("c"));
    }

    private static ICallerPrincipal Holding(string? subject, params string[] grants) =>
        new CallerPrincipal("bearer", grants, subject);

    [Fact]
    public void Define_ProducesTheRequirementTheAttributeWillUse() {
        var requirement = new CanManagePets().Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding(null, "pets:read", "pets:write"), Context));
        Assert.True(requirement.IsSatisfiedBy(Holding(null, "admin:*"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding(null, "pets:read"), Context));
    }

    /// <summary>
    /// Built once and kept. A policy is constructed once per closed attribute type and consulted on
    /// every request, so rebuilding the tree per read would put allocation on the request path.
    /// </summary>
    [Fact]
    public void Requirement_IsBuiltOnceAndReused() {
        var policy = new CountingPolicy();

        var first = policy.Requirement;
        var second = policy.Requirement;

        Assert.Same(first, second);
        Assert.Equal(1, policy.DefineCalls);
    }

    /// <summary>
    /// Nothing is built until something asks. A policy type that is referenced but never reached
    /// costs nothing.
    /// </summary>
    [Fact]
    public void Define_IsNotCalledUntilTheRequirementIsRead() {
        Assert.Equal(0, new CountingPolicy().DefineCalls);
    }

    [Fact]
    public void Policy_IsAnIAuthorizationPolicy() {
        IAuthorizationPolicy policy = new CanManagePets();

        Assert.NotNull(policy.Requirement);
    }

    /// <summary>
    /// The escape hatch reaches the principal and the context from inside a policy, which is the
    /// alternative to constructor-injecting dependencies into a type the attribute has to build.
    /// </summary>
    [Fact]
    public void Predicate_IsAvailableInsideAPolicy() {
        var requirement = new OwnsTheResource().Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding("owner", "pets:read"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("someone-else", "pets:read"), Context));
        Assert.True(requirement.RequiresContext);
    }

    [Fact]
    public void NamedCombinators_AreAvailableInsideAPolicy() {
        var requirement = new UsesNamedCombinators().Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding(null, "a", "b"), Context));
        Assert.True(requirement.IsSatisfiedBy(Holding(null, "c"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding(null, "a"), Context));
    }
}
