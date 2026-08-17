using Hardened.Requests.Abstract.Authorization;
using Combinator = Hardened.Requests.Abstract.Authorization.Requirement;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Requires the grants named.
/// </summary>
/// <remarks>
/// <para>
/// Every grant named here is required, and every authorization attribute on a handler is required.
/// Writing more of them can only narrow what is admitted, never widen it - which is what makes the
/// attribute safe to inherit, safe to write on a controller and a method at once, and safe for a
/// convention to add to.
/// </para>
/// <example>
/// <code>
/// [AuthorizeGrants("pets:read", "pets:write")]   // both required
/// [AuthorizeGrants("tenant:member")]             // ...and this as well
/// </code>
/// </example>
/// <para>
/// <b>Not sealed, because deriving from it is the hand-authored form.</b> A named attribute reads
/// better at the call site than a string does, is found by "go to references", and is renamed by a
/// refactor - so the grant vocabulary can be written once and spelled everywhere as a type:
/// </para>
/// <example>
/// <code>
/// public sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
///     public RequiresPetWriteAttribute() : base(Grants.Pets.Read, Grants.Pets.Write) { }
/// }
/// </code>
/// </example>
/// <para>
/// The string form remains what a generator emits from a specification, where a human never reads
/// the attribute and the values cannot be a typo because the generator read them out of the spec.
/// </para>
/// <para>
/// Alternatives - "this grant <em>or</em> that one" - are not expressible by stacking attributes,
/// deliberately. They belong inside a single <see cref="IAuthorizationPolicy"/>, where one author
/// writes one expression, rather than emerging from how many attributes happen to be present.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizeGrantsAttribute : Attribute, IAuthorizeAttribute {
    public AuthorizeGrantsAttribute(params string[] grants) {
        if (grants is null || grants.Length == 0) {
            throw new ArgumentException(
                "[AuthorizeGrants] must name at least one grant. An empty one would require nothing " +
                "while looking like it requires something, which is the one failure mode an " +
                "authorization attribute must not have. Use [AllowAnonymous] to make an operation " +
                "public on purpose.",
                nameof(grants));
        }

        Grants = grants;
        Requirement = Combinator.AllOf(Array.ConvertAll(grants, Combinator.Grant));
    }

    /// <summary>The grants named, in the order they were written.</summary>
    public IReadOnlyList<string> Grants { get; }

    public Requirement Requirement { get; }
}
