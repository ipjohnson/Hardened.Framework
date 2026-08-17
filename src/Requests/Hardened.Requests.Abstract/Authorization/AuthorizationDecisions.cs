namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// How contributors' decisions combine, and what a combined decision means.
/// </summary>
/// <remarks>
/// The rule lives here, in one place, written out. It is deliberately not expressed as a maximum
/// over <see cref="AuthorizationDecision"/>'s declaration order, even though the members happen to
/// be declared in the order it produces: that would make inserting a member in the wrong place a
/// silent change to what the framework permits, which is the same class of defect as the
/// <c>bool?</c> this replaced.
/// </remarks>
public static class AuthorizationDecisions {
    /// <summary>
    /// Combines two decisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An explicit <see cref="AuthorizationDecision.Deny"/> wins over everything: one contributor
    /// saying no is enough, however many said yes.
    /// </para>
    /// <para>
    /// A plain deny also wins over <see cref="AuthorizationDecision.DenyInsufficientAuthentication"/>.
    /// Both refuse the request, but the step-up answer sends the caller to re-authenticate, and
    /// doing that when another contributor has already said a better credential will not help is a
    /// wild goose chase.
    /// </para>
    /// <para>
    /// The operation is commutative and associative, so the answer does not depend on the order
    /// contributors were registered or consulted in.
    /// </para>
    /// </remarks>
    public static AuthorizationDecision Combine(AuthorizationDecision left, AuthorizationDecision right) {
        if (left == AuthorizationDecision.Deny || right == AuthorizationDecision.Deny) {
            return AuthorizationDecision.Deny;
        }

        if (left == AuthorizationDecision.DenyInsufficientAuthentication ||
            right == AuthorizationDecision.DenyInsufficientAuthentication) {
            return AuthorizationDecision.DenyInsufficientAuthentication;
        }

        if (left == AuthorizationDecision.Allow || right == AuthorizationDecision.Allow) {
            return AuthorizationDecision.Allow;
        }

        return AuthorizationDecision.Abstain;
    }

    /// <summary>
    /// Folds every contributor's decision into one.
    /// </summary>
    /// <remarks>
    /// An empty sequence is <see cref="AuthorizationDecision.Abstain"/>, which does not permit. That
    /// is the whole point: no contributors registered has to reach the same answer as every
    /// contributor abstaining, because they are indistinguishable from here.
    /// </remarks>
    public static AuthorizationDecision Combine(IEnumerable<AuthorizationDecision> decisions) {
        ArgumentNullException.ThrowIfNull(decisions);

        var combined = AuthorizationDecision.Abstain;

        foreach (var decision in decisions) {
            combined = Combine(combined, decision);

            // Nothing outranks a deny, so there is no reason to keep asking.
            if (combined == AuthorizationDecision.Deny) {
                break;
            }
        }

        return combined;
    }

    /// <summary>
    /// Whether a decision lets the request proceed.
    /// </summary>
    /// <remarks>
    /// Only <see cref="AuthorizationDecision.Allow"/> does. This is where "all-abstain denies"
    /// actually happens - <see cref="Combine(IEnumerable{AuthorizationDecision})"/> preserves an
    /// abstention rather than resolving it, so that a caller can still tell "nobody had an opinion"
    /// from "somebody said no" if it needs to.
    /// </remarks>
    public static bool Permits(this AuthorizationDecision decision) =>
        decision == AuthorizationDecision.Allow;
}
