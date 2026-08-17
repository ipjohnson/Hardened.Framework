using Hardened.Requests.Abstract.Execution;
using Combinator = Hardened.Requests.Abstract.Authorization.Requirement;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// A named policy, written once and referenced by <c>[Authorize&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
/// <para>
/// One type per policy the application actually has a name for, not one per permutation of grants.
/// The permutations live in <see cref="Define"/>.
/// </para>
/// <example>
/// <code>
/// public class CanManagePets : AuthorizationPolicy {
///     protected override Requirement Define() =>
///         (Grant(Grants.PetstoreAuth.PetsRead) &amp; Grant(Grants.PetstoreAuth.PetsWrite))
///         | Grant(Grants.AdminKey.All);
/// }
/// </code>
/// </example>
/// <para>
/// <see cref="Define"/> runs once per policy type and the tree is kept, so building it may cost
/// whatever it needs to. A policy is constructed by the attribute that names it and therefore needs
/// a parameterless constructor; dependencies belong in <see cref="Predicate"/>, which is handed the
/// execution context and can resolve anything from it.
/// </para>
/// </remarks>
public abstract class AuthorizationPolicy : IAuthorizationPolicy {
    private Requirement? _requirement;

    public Requirement Requirement => _requirement ??= Define();

    /// <summary>
    /// Builds the requirement. Called once.
    /// </summary>
    protected abstract Requirement Define();

    /// <inheritdoc cref="Requirement.Grant"/>
    protected static Requirement Grant(string grant) => Combinator.Grant(grant);

    /// <inheritdoc cref="Requirement.AllOf"/>
    protected static Requirement AllOf(params Requirement[] requirements) =>
        Combinator.AllOf(requirements);

    /// <inheritdoc cref="Requirement.AnyOf"/>
    protected static Requirement AnyOf(params Requirement[] requirements) =>
        Combinator.AnyOf(requirements);

    /// <inheritdoc cref="Requirement.Predicate"/>
    protected static Requirement Predicate(
        Func<ICallerPrincipal, IExecutionContext, bool> predicate,
        string? description = null) =>
        Combinator.Predicate(predicate, description);
}
