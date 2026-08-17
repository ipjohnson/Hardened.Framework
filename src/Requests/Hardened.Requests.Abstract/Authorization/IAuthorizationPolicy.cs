namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What an operation requires. One of the two halves of an authorization decision.
/// </summary>
/// <remarks>
/// This answers "what does this <em>operation</em> require"; <see cref="IActivityAuthorizationService"/>
/// answers "does this <em>caller</em> hold these grants". Keeping them apart is what lets grants be
/// resolved from a store - a permissions table, a per-tenant role expansion - without a policy
/// knowing where they came from.
/// </remarks>
public interface IAuthorizationPolicy {
    /// <summary>
    /// The requirement this policy imposes. Built once and reused.
    /// </summary>
    Requirement Requirement { get; }
}
