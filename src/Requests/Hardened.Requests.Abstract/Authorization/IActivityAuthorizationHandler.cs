using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// One contributor to the answer <see cref="IActivityAuthorizationService"/> composes.
/// </summary>
/// <remarks>
/// <para>
/// Several may be registered, each knowing about a different source of grants, and each is free to
/// have no opinion. A handler that does not recognise the operation, the tenant, or the grants being
/// asked about returns <see cref="GrantResolution.Abstained"/> - it must not vouch for a grant to
/// mean "not my problem", because vouching is a decision and abstaining is the absence of one.
/// </para>
/// <para>
/// This is the seam for grants that live in a store rather than in the token, which is what makes
/// the resource-server design independent of how tokens are issued.
/// </para>
/// </remarks>
public interface IActivityAuthorizationHandler {
    /// <summary>
    /// Says which of <paramref name="grants"/> this handler's source of truth gives the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked once with the whole list, so a handler backed by a table or a service makes one round
    /// trip rather than one per grant. It is the reason this returns a set rather than a yes or no:
    /// a single verdict over several grants can only mean "all of them", which would refuse a caller
    /// who holds either of two alternatives.
    /// </para>
    /// <para>
    /// Answer only about the grants asked for. Returning others is answering a question nobody put,
    /// and nothing downstream will look at them.
    /// </para>
    /// </remarks>
    ValueTask<GrantResolution> Resolve(IExecutionContext context, IReadOnlyList<string> grants);
}
