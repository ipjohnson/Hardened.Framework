using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// What a contributor found, and how several contributors' findings merge.
///
/// <para>
/// The shape exists so that one call can answer for many grants. A contributor asked "does this
/// caller hold these grants" can only answer the conjunction, so a requirement of <c>a | b</c>
/// resolved in one call would come back refused for a caller holding exactly one of them. Returning
/// the subset means one round trip and no lost structure, and these pin both halves of that.
/// </para>
/// </summary>
public class GrantResolutionTests {

    #region building one

    [Fact]
    public void Abstained_FindsNothingAndSaysNothing() {
        Assert.Empty(GrantResolution.Abstained.Granted);
        Assert.Equal(AuthorizationDecision.Abstain, GrantResolution.Abstained.Decision);
    }

    [Fact]
    public void Granting_CarriesTheGrantsAndNoVerdict() {
        var resolution = GrantResolution.Granting("pets:read", "pets:write");

        Assert.Contains("pets:read", resolution.Granted);
        Assert.Contains("pets:write", resolution.Granted);
        Assert.Equal(AuthorizationDecision.Abstain, resolution.Decision);
    }

    [Fact]
    public void Granting_NothingIsTheSameAsAbstaining() {
        Assert.Same(GrantResolution.Abstained, GrantResolution.Granting());
    }

    [Fact]
    public void Refusing_CarriesTheVerdictAndNoGrants() {
        var resolution = GrantResolution.Refusing(AuthorizationDecision.Deny);

        Assert.Empty(resolution.Granted);
        Assert.Equal(AuthorizationDecision.Deny, resolution.Decision);
    }

    /// <summary>
    /// Ordinal, for the same reason a grant is matched ordinally everywhere else: it is a value an
    /// authorization server issued, not prose.
    /// </summary>
    [Fact]
    public void Granted_MatchesOrdinally() {
        var resolution = GrantResolution.Granting("pets:read");

        Assert.Contains("pets:read", resolution.Granted);
        Assert.DoesNotContain("Pets:Read", resolution.Granted);
    }

    #endregion

    #region merging

    /// <summary>
    /// The grants union, because each contributor knows about a different source and none of them
    /// sees the whole picture.
    /// </summary>
    [Fact]
    public void Combine_UnionsWhatEachContributorFound() {
        var combined = GrantResolution.Combine(
            GrantResolution.Granting("a", "b"),
            GrantResolution.Granting("b", "c"));

        Assert.Equal(["a", "b", "c"], combined.Granted.Order());
    }

    [Fact]
    public void Combine_WithAnAbstentionKeepsWhatWasFound() {
        var found = GrantResolution.Granting("a");

        Assert.Equal(["a"], GrantResolution.Combine(found, GrantResolution.Abstained).Granted);
        Assert.Equal(["a"], GrantResolution.Combine(GrantResolution.Abstained, found).Granted);
    }

    /// <summary>
    /// A refusal outranks grants another contributor vouched for. One contributor saying no is
    /// enough, and it being a different one from the one that said yes does not change that.
    /// </summary>
    [Fact]
    public void Combine_LetsARefusalOutrankGrantsFoundElsewhere() {
        var combined = GrantResolution.Combine(
            GrantResolution.Granting("a"),
            GrantResolution.Refusing(AuthorizationDecision.Deny));

        Assert.Equal(AuthorizationDecision.Deny, combined.Decision);

        // The grant is still reported; the verdict is what refuses, and keeping both means the
        // refusal can say which grant it was about.
        Assert.Contains("a", combined.Granted);
    }

    [Fact]
    public void Combine_ComposesVerdictsByTheUsualRule() {
        var combined = GrantResolution.Combine(
            GrantResolution.Refusing(AuthorizationDecision.DenyInsufficientAuthentication),
            GrantResolution.Refusing(AuthorizationDecision.Deny));

        Assert.Equal(AuthorizationDecision.Deny, combined.Decision);
    }

    /// <summary>
    /// Order-independent, so the answer does not depend on which contributor a module happened to
    /// register first.
    /// </summary>
    [Fact]
    public void Combine_DoesNotDependOnOrder() {
        var left = GrantResolution.Granting("a");
        var right = new GrantResolution(
            new HashSet<string> { "b" }, AuthorizationDecision.DenyInsufficientAuthentication);

        var forwards = GrantResolution.Combine(left, right);
        var backwards = GrantResolution.Combine(right, left);

        Assert.Equal(forwards.Decision, backwards.Decision);
        Assert.Equal(forwards.Granted.Order(), backwards.Granted.Order());
    }

    #endregion
}
