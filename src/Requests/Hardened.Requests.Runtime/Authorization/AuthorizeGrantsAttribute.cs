using Hardened.Requests.Abstract.Authorization;
using Combinator = Hardened.Requests.Abstract.Authorization.Requirement;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Requires the grants named. The form a generator emits from a specification.
/// </summary>
/// <remarks>
/// <para>
/// Grants within one attribute are <b>AND</b>; repeating the attribute is <b>OR</b>, which is
/// exactly the shape OpenAPI's <c>security</c> has - a list of requirement objects, any of which
/// admits the request.
/// </para>
/// <example>
/// <code>
/// [AuthorizeGrants("pets:read", "pets:write")]   // both required
/// [AuthorizeGrants("admin:*")]                   // ...or this instead
/// </code>
/// </example>
/// <para>
/// <b>Strings are acceptable here precisely because a human never writes them.</b> The generator
/// read them out of the specification, so they cannot be a typo the way a hand-written one can. The
/// hand-authored form is <c>[Authorize&lt;T&gt;]</c>, which is typed and rename-safe; this one
/// trades that for being able to carry arbitrary structure that nobody has to read.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AuthorizeGrantsAttribute : Attribute, IAuthorizeAttribute {
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

    /// <summary>The grants named, in the order the specification listed them.</summary>
    public IReadOnlyList<string> Grants { get; }

    public Requirement Requirement { get; }
}
