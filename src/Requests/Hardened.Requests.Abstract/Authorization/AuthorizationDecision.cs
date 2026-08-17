namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What one contributor concluded about a caller.
/// </summary>
/// <remarks>
/// <para>
/// This was a <c>bool?</c>, where null meant abstain. Three states were right; naming only two of
/// them was not. The composition rule had to be recited at every call site, and the natural-reading
/// mistake - <c>if (decision != false)</c> - turns "every contributor abstained" into allow, which
/// is exactly the silent failure the rule exists to prevent.
/// </para>
/// <para>
/// <b>The numeric values carry no meaning</b> beyond <see cref="Abstain"/> being zero, so that a
/// default-initialised decision is the one that denies. In particular the declaration order is not
/// the composition rule: that is written out in <see cref="AuthorizationDecisions.Combine(AuthorizationDecision, AuthorizationDecision)"/>,
/// because a security rule derived from the order members happen to appear in changes silently when
/// someone inserts one.
/// </para>
/// </remarks>
public enum AuthorizationDecision {
    /// <summary>
    /// No opinion. A contributor that does not recognise the operation says this rather than
    /// guessing.
    /// </summary>
    /// <remarks>
    /// Abstaining is not permitting. "Every contributor abstained" and "no contributors are
    /// registered" are the same observable state, and both deny - a framework whose authorization
    /// turns itself off when its handlers are not registered has the worst possible failure mode.
    /// </remarks>
    Abstain = 0,

    /// <summary>
    /// The caller may proceed. The only decision that permits anything.
    /// </summary>
    Allow,

    /// <summary>
    /// The credential is valid but not strong enough for this operation - RFC 9470 step-up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one case where an authenticated caller is answered with 401 rather than 403, because the
    /// remedy is a better credential rather than more grants. Answering 403 would tell the client to
    /// stop, when in fact it can succeed by authenticating again more strongly.
    /// </para>
    /// <para>
    /// Named for the error code it produces - <c>WWW-Authenticate: Bearer
    /// error="insufficient_user_authentication"</c> - so that the wire response and the source agree
    /// on a term someone can search for.
    /// </para>
    /// <para>
    /// A contributor comparing an <c>acr</c> or <c>amr</c> claim against what the operation demands
    /// is the thing that knows this, which is why it travels on the decision rather than being
    /// inferred later from the principal.
    /// </para>
    /// </remarks>
    DenyInsufficientAuthentication,

    /// <summary>
    /// The caller may not proceed and a better credential would not help - 403.
    /// </summary>
    Deny,
}
