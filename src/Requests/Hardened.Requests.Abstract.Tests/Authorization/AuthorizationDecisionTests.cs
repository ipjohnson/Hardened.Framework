using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// The composition rule, pinned as a table.
///
/// <para>
/// This is the rule that decides what happens when contributors disagree, and every way of getting
/// it wrong fails open rather than loudly. It is written out in <c>Combine</c> rather than derived
/// from the enum's declaration order precisely so that it can be tested as a table - and the table
/// is exhaustive, so adding a member to <see cref="AuthorizationDecision"/> without deciding how it
/// composes fails a test rather than quietly taking whatever the last branch returns.
/// </para>
/// </summary>
public class AuthorizationDecisionTests {

    private static readonly AuthorizationDecision[] AllDecisions =
        Enum.GetValues<AuthorizationDecision>();

    /// <summary>
    /// Zero is abstain, so a default-initialised decision is the one that does not permit. A struct
    /// field, an array slot or an uninitialised local all land on the safe answer.
    /// </summary>
    [Fact]
    public void Default_IsAbstain() {
        Assert.Equal(AuthorizationDecision.Abstain, default);
        Assert.Equal(0, (int)AuthorizationDecision.Abstain);
    }

    #region what permits

    /// <summary>
    /// Only allow permits. Abstain is not a quiet yes, and neither kind of deny is.
    /// </summary>
    [Fact]
    public void Permits_IsTrueForAllowAndNothingElse() {
        Assert.True(AuthorizationDecision.Allow.Permits());

        Assert.False(AuthorizationDecision.Abstain.Permits());
        Assert.False(AuthorizationDecision.Deny.Permits());
        Assert.False(AuthorizationDecision.DenyInsufficientAuthentication.Permits());
    }

    /// <summary>
    /// Stated as a property over every member rather than the three literals above, so a new member
    /// defaults to not permitting unless someone says otherwise here.
    /// </summary>
    [Fact]
    public void Permits_IsFalseForEveryMemberExceptAllow() {
        Assert.All(
            AllDecisions.Where(d => d != AuthorizationDecision.Allow),
            d => Assert.False(d.Permits()));
    }

    #endregion

    #region the table

    /// <summary>
    /// Every unordered pair, asserted in both orders - which covers all sixteen combinations and
    /// commutativity at the same time. Commutativity is what makes the answer independent of the
    /// order handlers were registered in.
    /// </summary>
    [Theory]
    // abstain yields to anything with an opinion
    [InlineData(AuthorizationDecision.Abstain, AuthorizationDecision.Abstain, AuthorizationDecision.Abstain)]
    [InlineData(AuthorizationDecision.Abstain, AuthorizationDecision.Allow, AuthorizationDecision.Allow)]
    [InlineData(AuthorizationDecision.Abstain, AuthorizationDecision.DenyInsufficientAuthentication, AuthorizationDecision.DenyInsufficientAuthentication)]
    [InlineData(AuthorizationDecision.Abstain, AuthorizationDecision.Deny, AuthorizationDecision.Deny)]
    // allow is overridden by either refusal
    [InlineData(AuthorizationDecision.Allow, AuthorizationDecision.Allow, AuthorizationDecision.Allow)]
    [InlineData(AuthorizationDecision.Allow, AuthorizationDecision.DenyInsufficientAuthentication, AuthorizationDecision.DenyInsufficientAuthentication)]
    [InlineData(AuthorizationDecision.Allow, AuthorizationDecision.Deny, AuthorizationDecision.Deny)]
    // a plain deny outranks a step-up: a better credential would not help
    [InlineData(AuthorizationDecision.DenyInsufficientAuthentication, AuthorizationDecision.DenyInsufficientAuthentication, AuthorizationDecision.DenyInsufficientAuthentication)]
    [InlineData(AuthorizationDecision.DenyInsufficientAuthentication, AuthorizationDecision.Deny, AuthorizationDecision.Deny)]
    [InlineData(AuthorizationDecision.Deny, AuthorizationDecision.Deny, AuthorizationDecision.Deny)]
    public void Combine_FollowsTheStatedTableInEitherOrder(
        AuthorizationDecision left, AuthorizationDecision right, AuthorizationDecision expected) {
        Assert.Equal(expected, AuthorizationDecisions.Combine(left, right));
        Assert.Equal(expected, AuthorizationDecisions.Combine(right, left));
    }

    /// <summary>
    /// The table above must cover every member. A member added without a row here would otherwise
    /// fall through <c>Combine</c>'s branches to abstain, and abstaining is a decision this type
    /// exists to stop anyone making by accident.
    /// </summary>
    [Fact]
    public void Combine_TableCoversEveryMember() {
        Assert.Equal(4, AllDecisions.Length);

        Assert.Equal(
            [
                AuthorizationDecision.Abstain,
                AuthorizationDecision.Allow,
                AuthorizationDecision.DenyInsufficientAuthentication,
                AuthorizationDecision.Deny,
            ],
            AllDecisions);
    }

    /// <summary>
    /// Associative as well as commutative, over every triple. Together those are what let the fold
    /// consult handlers in any order, in parallel, or short-circuit partway and still agree.
    /// </summary>
    [Fact]
    public void Combine_IsAssociativeOverEveryTriple() {
        foreach (var a in AllDecisions) {
            foreach (var b in AllDecisions) {
                foreach (var c in AllDecisions) {
                    Assert.Equal(
                        AuthorizationDecisions.Combine(AuthorizationDecisions.Combine(a, b), c),
                        AuthorizationDecisions.Combine(a, AuthorizationDecisions.Combine(b, c)));
                }
            }
        }
    }

    [Fact]
    public void Combine_IsIdempotent() {
        Assert.All(AllDecisions, d => Assert.Equal(d, AuthorizationDecisions.Combine(d, d)));
    }

    #endregion

    #region folding contributors

    /// <summary>
    /// The case the rule is really about: no handlers registered has to reach the same answer as
    /// every handler abstaining, because from here they are the same observable state. A framework
    /// whose authorization switches itself off when its handlers are missing has the worst failure
    /// mode available to it.
    /// </summary>
    [Fact]
    public void Combine_OfNothingIsAbstainAndDoesNotPermit() {
        var decision = AuthorizationDecisions.Combine([]);

        Assert.Equal(AuthorizationDecision.Abstain, decision);
        Assert.False(decision.Permits());
    }

    [Fact]
    public void Combine_OfAllAbstentionsDoesNotPermit() {
        var decision = AuthorizationDecisions.Combine([
            AuthorizationDecision.Abstain,
            AuthorizationDecision.Abstain,
        ]);

        Assert.Equal(AuthorizationDecision.Abstain, decision);
        Assert.False(decision.Permits());
    }

    [Fact]
    public void Combine_OneAllowAmongAbstentionsPermits() {
        var decision = AuthorizationDecisions.Combine([
            AuthorizationDecision.Abstain,
            AuthorizationDecision.Allow,
            AuthorizationDecision.Abstain,
        ]);

        Assert.True(decision.Permits());
    }

    /// <summary>
    /// One refusal is enough, however many contributors permitted.
    /// </summary>
    [Fact]
    public void Combine_OneDenyAmongManyAllowsDenies() {
        var decision = AuthorizationDecisions.Combine([
            AuthorizationDecision.Allow,
            AuthorizationDecision.Allow,
            AuthorizationDecision.Deny,
            AuthorizationDecision.Allow,
        ]);

        Assert.Equal(AuthorizationDecision.Deny, decision);
    }

    /// <summary>
    /// Registration order is not part of the answer. Two handlers disagreeing must not resolve
    /// differently because a module happened to register one first.
    /// </summary>
    [Fact]
    public void Combine_DoesNotDependOnTheOrderContributorsAreConsulted() {
        AuthorizationDecision[] decisions = [
            AuthorizationDecision.Allow,
            AuthorizationDecision.Abstain,
            AuthorizationDecision.DenyInsufficientAuthentication,
        ];

        Assert.Equal(
            AuthorizationDecisions.Combine(decisions),
            AuthorizationDecisions.Combine(decisions.Reverse()));
    }

    /// <summary>
    /// A deny settles it, so later contributors are not consulted at all. Intentional and worth
    /// pinning: a handler behind this one may be a database round trip or an entitlement service
    /// call, and there is no answer it could give that would change the result.
    /// </summary>
    [Fact]
    public void Combine_StopsConsultingContributorsOnceDenied() {
        var consulted = 0;

        IEnumerable<AuthorizationDecision> Contributors() {
            consulted++;
            yield return AuthorizationDecision.Deny;

            consulted++;
            yield return AuthorizationDecision.Allow;
        }

        Assert.Equal(AuthorizationDecision.Deny, AuthorizationDecisions.Combine(Contributors()));
        Assert.Equal(1, consulted);
    }

    /// <summary>
    /// A step-up does not short-circuit, because a plain deny still outranks it and a later
    /// contributor may yet produce one.
    /// </summary>
    [Fact]
    public void Combine_KeepsConsultingAfterAStepUpBecauseADenyStillOutranksIt() {
        var decision = AuthorizationDecisions.Combine([
            AuthorizationDecision.DenyInsufficientAuthentication,
            AuthorizationDecision.Deny,
        ]);

        Assert.Equal(AuthorizationDecision.Deny, decision);
    }

    [Fact]
    public void Combine_RejectsANullSequence() {
        Assert.Throws<ArgumentNullException>(() => AuthorizationDecisions.Combine(null!));
    }

    #endregion
}
