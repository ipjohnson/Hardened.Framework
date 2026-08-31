using System.Reflection;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The three attribute forms, and the requirement each produces.
///
/// <para>
/// An attribute written on a handler already reaches the runtime as handler metadata, so what these
/// pin is the other half: that both forms arrive as one <see cref="Requirement"/> each through a
/// single interface, so the pipeline never has to know which form it came from.
/// </para>
/// </summary>
public class AuthorizeAttributeTests {

    private static readonly IExecutionContext Context = Substitute.For<IExecutionContext>();

    private class CanManagePets : AuthorizationPolicy {
        protected override Requirement Define() =>
            (Grant("pets:read") & Grant("pets:write")) | Grant("admin:*");
    }

    private static ICallerPrincipal Holding(params string[] grants) =>
        new CallerPrincipal("bearer", grants);

    /// <summary>The scheme the typed forms name. The shape attribute is the document's business.</summary>
    [HttpAuthenticationScheme("bearer")]
    private sealed class BearerAuth : IAuthenticationScheme;

    #region the typed forms

    /// <summary>
    /// The single type parameter is the scheme, and the requirement is "an authenticated
    /// caller" - the spelling that had none before the slot was re-meant.
    /// </summary>
    [Fact]
    public void AuthorizeScheme_RequiresAnAuthenticatedCaller() {
        var requirement = new AuthorizeAttribute<BearerAuth>().Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding(), Context));
        Assert.False(requirement.IsSatisfiedBy(AnonymousCallerPrincipal.Instance, Context));
    }

    /// <summary>
    /// The policy rides second and its tree still decides, conjoined with authenticated - so a
    /// context-only policy cannot admit an anonymous caller by accident.
    /// </summary>
    [Fact]
    public void AuthorizeSchemeAndPolicy_YieldsThePolicysRequirement() {
        var requirement = new AuthorizeAttribute<BearerAuth, CanManagePets>().Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding("pets:read", "pets:write"), Context));
        Assert.True(requirement.IsSatisfiedBy(Holding("admin:*"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
        Assert.False(requirement.IsSatisfiedBy(AnonymousCallerPrincipal.Instance, Context));
    }

    /// <summary>
    /// One requirement per closed type, however many handlers carry the attribute. Writing
    /// <c>[Authorize&lt;BearerAuth, CanManagePets&gt;]</c> on a hundred handlers costs one tree.
    /// </summary>
    [Fact]
    public void Authorize_SharesOneRequirementAcrossEveryInstance() {
        Assert.Same(
            new AuthorizeAttribute<BearerAuth, CanManagePets>().Requirement,
            new AuthorizeAttribute<BearerAuth, CanManagePets>().Requirement);
    }

    #endregion

    #region the generated form

    /// <summary>
    /// Grants within one attribute are AND, which is what a single OpenAPI requirement object means.
    /// </summary>
    [Fact]
    public void AuthorizeGrants_RequiresAllTheGrantsInOneAttribute() {
        var requirement = new AuthorizeGrantsAttribute("pets:read", "pets:write").Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding("pets:read", "pets:write"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
    }

    [Fact]
    public void AuthorizeGrants_WithOneGrantRequiresThatGrant() {
        var requirement = new AuthorizeGrantsAttribute("admin:*").Requirement;

        Assert.True(requirement.IsSatisfiedBy(Holding("admin:*"), Context));
        Assert.False(requirement.IsSatisfiedBy(Holding("pets:read"), Context));
    }

    /// <summary>
    /// Repeating the attribute is OR, which is the outer list of OpenAPI's <c>security</c>. Without
    /// AllowMultiple the generator could not express a spec that offers two alternative schemes at
    /// all - and <c>params string[]</c> alone flattens the OR away, which is why the structure lives
    /// in the repetition rather than in the arguments.
    /// </summary>
    [Fact]
    public void AuthorizeGrants_MayBeRepeatedToExpressAnAlternative() {
        var usage = typeof(AuthorizeGrantsAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.True(usage.AllowMultiple);
    }

    /// <summary>
    /// An empty one would require nothing while looking like it requires something - the one failure
    /// mode an authorization attribute must not have. It fails where it is written rather than
    /// silently admitting every caller.
    /// </summary>
    [Fact]
    public void AuthorizeGrants_WithNoGrantsThrows() {
        Assert.Throws<ArgumentException>(() => new AuthorizeGrantsAttribute());
    }

    [Fact]
    public void AuthorizeGrants_KeepsTheGrantsItWasGiven() {
        Assert.Equal(
            ["pets:read", "pets:write"],
            new AuthorizeGrantsAttribute("pets:read", "pets:write").Grants);
    }

    #endregion

    #region one shape for both

    /// <summary>
    /// The point of the interface: metadata arrives as <c>object[]</c>, and the pipeline collects
    /// requirements out of it without knowing the closed type of a <c>[Authorize&lt;T&gt;]</c> - a
    /// distinction that cannot be made by reflection over an open generic without giving up
    /// trimming.
    /// </summary>
    [Fact]
    public void BothFormsAreFoundThroughOneInterface() {
        object[] metadata = [
            new AuthorizeAttribute<BearerAuth>(),
            new AuthorizeAttribute<BearerAuth, CanManagePets>(),
            new AuthorizeGrantsAttribute("pets:read"),
            "something else entirely",
        ];

        var requirements = metadata.OfType<IAuthorizeAttribute>().Select(a => a.Requirement).ToArray();

        Assert.Equal(3, requirements.Length);
        Assert.All(requirements, Assert.NotNull);
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> deliberately carries no requirement. Giving it a permissive one would
    /// let it be combined with a real requirement and win, which is the opposite of what an opt-out
    /// should do when someone writes both by mistake.
    /// </summary>
    [Fact]
    public void AllowAnonymousCarriesNoRequirement() {
        Assert.False(typeof(IAuthorizeAttribute).IsAssignableFrom(typeof(AllowAnonymousAttribute)));
    }

    [Theory]
    [InlineData(typeof(AuthorizeAttribute<BearerAuth>))]
    [InlineData(typeof(AuthorizeAttribute<BearerAuth, CanManagePets>))]
    [InlineData(typeof(AuthorizeGrantsAttribute))]
    [InlineData(typeof(AllowAnonymousAttribute))]
    public void EveryFormAppliesToAMethodOrAClass(Type attributeType) {
        var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
    }

    #endregion
}
