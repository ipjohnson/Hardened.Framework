using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The authorization a description declared, once it is on a handler.
/// </summary>
/// <remarks>
/// A generated handler carries it as one more entry in its metadata, which is what makes it compose
/// with whatever the implementation declared rather than replacing it. These pin that composition,
/// because the failure mode is silent in both directions: a described requirement that replaced an
/// attribute would drop a rule somebody wrote, and one that was ignored would drop the contract's.
/// </remarks>
public class DescribedAuthorizationTests {

    /// <summary>
    /// It is recognised the same way an attribute is - through the interface, not by type name.
    /// </summary>
    [Fact]
    public void ADescribedRequirementReachesTheHandler() {
        var requirement = Requirement.Grant("pets:write");

        var result = IExecutionRequestHandlerInfo.RequirementFrom(
            [new DescribedAuthorization(requirement)]);

        Assert.NotNull(result);
        Assert.Equal(new[] { "pets:write" }, result!.RequiredGrants);
    }

    /// <summary>
    /// It conjoins with an attribute the implementation wrote, rather than replacing it.
    /// </summary>
    /// <remarks>
    /// The reason this is metadata rather than the <c>requirement</c> parameter on
    /// <c>ExecutionRequestHandlerInfo</c>: that reads <c>requirement ?? RequirementFrom(Metadata)</c>,
    /// so passing it there would have silenced the attribute instead of composing with it.
    /// </remarks>
    [Fact]
    public void ADescribedRequirementNarrowsRatherThanReplaces() {
        var result = IExecutionRequestHandlerInfo.RequirementFrom([
            new DescribedAuthorization(Requirement.Grant("pets:write")),
            new AuthorizeGrantsAttribute("tenant:member")
        ]);

        Assert.NotNull(result);
        Assert.Contains("pets:write", result!.RequiredGrants);
        Assert.Contains("tenant:member", result.RequiredGrants);
    }

    /// <summary>
    /// An alternative names every grant that would have satisfied it, so a refusal can say what it
    /// wanted rather than picking one branch arbitrarily.
    /// </summary>
    [Fact]
    public void AnAlternativeNamesEveryGrantThatWouldSatisfyIt() {
        var described = new DescribedAuthorization(
            Requirement.AnyOf(
                Requirement.Grant("pets:read"),
                Requirement.Grant("admin:all")));

        Assert.Equal(
            new[] { "pets:read", "admin:all" },
            described.Requirement.RequiredGrants);
    }

    /// <summary>
    /// A branch that only requires a caller is a requirement, not the absence of one - which is what
    /// keeps an OR containing it from being satisfied by everybody.
    /// </summary>
    [Fact]
    public void AuthenticatedIsARequirementInItsOwnRight() {
        var anonymous = AnonymousCallerPrincipal.Instance;

        var described = new DescribedAuthorization(
            Requirement.AnyOf(Requirement.Grant("pets:read"), Requirement.Authenticated()));

        Assert.False(described.Requirement.IsSatisfiedBy(anonymous, null!));
    }
}
