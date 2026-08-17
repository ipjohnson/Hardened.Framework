using System.Collections.Frozen;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What a contributor found: which of the grants it was asked about the caller holds, and any
/// verdict that overrides the arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a set rather than a decision.</b> A contributor asked "does this caller hold these grants"
/// can only answer the conjunction, so a requirement of <c>a | b</c> asked in one call comes back
/// refused for a caller who holds exactly one of them - the or has been flattened into an and. The
/// alternative was asking one grant at a time, which preserves the structure and costs a round trip
/// per grant against anything backed by a store. Returning the subset does both: one call, and the
/// requirement is re-walked against what came back, whatever shape it has.
/// </para>
/// <para>
/// <see cref="Granted"/> is evidence and <see cref="Decision"/> is an override. A contributor that
/// merely knows what the caller holds fills in the first and leaves the second
/// <see cref="AuthorizationDecision.Abstain"/>; one that wants to refuse the request outright, or to
/// say the credential is too weak whatever grants are held, uses the second.
/// </para>
/// </remarks>
public sealed class GrantResolution {
    /// <summary>Nothing found, nothing to say.</summary>
    public static readonly GrantResolution Abstained =
        new(FrozenSet<string>.Empty, AuthorizationDecision.Abstain);

    public GrantResolution(
        IReadOnlySet<string> granted,
        AuthorizationDecision decision = AuthorizationDecision.Abstain) {
        ArgumentNullException.ThrowIfNull(granted);

        Granted = granted;
        Decision = decision;
    }

    /// <summary>The caller holds these, of the grants asked about.</summary>
    public static GrantResolution Granting(params string[] granted) =>
        Granting((IEnumerable<string>)granted);

    /// <inheritdoc cref="Granting(string[])"/>
    public static GrantResolution Granting(IEnumerable<string> granted) {
        ArgumentNullException.ThrowIfNull(granted);

        var set = granted.ToFrozenSet(StringComparer.Ordinal);

        return set.Count == 0 ? Abstained : new GrantResolution(set);
    }

    /// <summary>
    /// A verdict that stands whatever grants the caller holds - an outright refusal, or a demand for
    /// a stronger credential.
    /// </summary>
    public static GrantResolution Refusing(AuthorizationDecision decision) =>
        new(FrozenSet<string>.Empty, decision);

    /// <summary>
    /// The subset of the requested grants this contributor vouches for.
    /// </summary>
    /// <remarks>
    /// Only ever grants that were asked about. A contributor returning something else is answering a
    /// question nobody asked, and the requirement being evaluated would not look at it anyway.
    /// </remarks>
    public IReadOnlySet<string> Granted { get; }

    /// <summary>
    /// A verdict that overrides <see cref="Granted"/>, or
    /// <see cref="AuthorizationDecision.Abstain"/> when there is none.
    /// </summary>
    public AuthorizationDecision Decision { get; }

    /// <summary>
    /// Merges two contributors' findings.
    /// </summary>
    /// <remarks>
    /// The grants union, because each contributor knows about a different source and none of them
    /// sees the whole picture. The verdicts compose by
    /// <see cref="AuthorizationDecisions.Combine(AuthorizationDecision, AuthorizationDecision)"/>, so
    /// a refusal still outranks everything - including grants another contributor vouched for.
    /// </remarks>
    public static GrantResolution Combine(GrantResolution left, GrantResolution right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var decision = AuthorizationDecisions.Combine(left.Decision, right.Decision);

        if (right.Granted.Count == 0) {
            return left.Decision == decision ? left : new GrantResolution(left.Granted, decision);
        }

        if (left.Granted.Count == 0) {
            return right.Decision == decision ? right : new GrantResolution(right.Granted, decision);
        }

        var union = new HashSet<string>(left.Granted, StringComparer.Ordinal);
        union.UnionWith(right.Granted);

        return new GrantResolution(union, decision);
    }
}
